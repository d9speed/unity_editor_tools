// D3D11 -> NVENC HEVC. All graphics/NVENC calls run on Unity's render thread.
// Only compressed packets enter the CPU writer queue. No staging/readback texture.
#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wrl/client.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <cstdint>
#include <cstring>
#include <deque>
#include <memory>
#include <mutex>
#include <stdexcept>
#include <string>
#include <thread>
#include <unordered_map>
#include <vector>
#include "include/nvEncodeAPI.h"
using Microsoft::WRL::ComPtr;
using clock_type = std::chrono::steady_clock;
#define API extern "C" __declspec(dllexport)

struct gpu_stats {
    int32_t state, api_version, width, height;
    int64_t captured, submitted, encoded, written, bytes, backpressure, gpu_copies, raw_readback_bytes;
};
static void require(bool ok, const std::string& message) { if (!ok) throw std::runtime_error(message); }
static void hr_check(HRESULT hr, const char* operation) {
    if (FAILED(hr)) throw std::runtime_error(std::string(operation) + " HRESULT=" + std::to_string(static_cast<uint32_t>(hr)));
}
struct slot {
    ComPtr<ID3D11Texture2D> texture;
    ComPtr<ID3D11Query> copy_done;
    NV_ENC_REGISTERED_PTR registered = nullptr;
    NV_ENC_INPUT_PTR mapped = nullptr;
    NV_ENC_OUTPUT_PTR output = nullptr;
    HANDLE done = nullptr;
    bool event_registered = false, busy = false;
    uint64_t frame = 0;
};
class session {
public:
    ComPtr<ID3D11Texture2D> source;
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> context;
    NV_ENCODE_API_FUNCTION_LIST api = {};
    HMODULE nv_module = nullptr;
    void* encoder = nullptr;
    NV_ENC_BUFFER_FORMAT format = NV_ENC_BUFFER_FORMAT_UNDEFINED;
    std::vector<slot> slots;
    std::deque<int> copying, encoding;
    int width, height, fps, bitrate, cq;
    std::wstring path;
    std::atomic<int> state{0}; // 0 pending, 1 recording, 2 draining, 3 closed, 4 failed
    std::atomic<int> driver_api{0};
    std::atomic<bool> finished{false};
    HANDLE eos_done = nullptr;
    bool eos_registered = false;
    std::atomic<int64_t> captured{0}, submitted{0}, encoded{0}, written{0}, bytes{0}, pressure{0}, copies{0};
    std::mutex error_mutex, writer_mutex;
    std::string error;
    std::condition_variable writer_cv;
    std::deque<std::vector<uint8_t>> packets;
    size_t queued_bytes = 0;
    bool writer_stop = false;
    std::atomic<bool> writer_failed{false};
    std::thread writer;
    HANDLE file = INVALID_HANDLE_VALUE;

    session(void* texture, int w, int h, int rate, int bps, int quality, const wchar_t* filename, int buffers)
        : width(w), height(h), fps(rate), bitrate(bps), cq(quality), path(filename ? filename : L"") {
        require(texture && w >= 16 && h >= 16 && !(w % 2) && !(h % 2) && rate >= 1 && rate <= 240,
            "Invalid texture, dimensions (even, >= 16) or fps (1..240).");
        require(buffers >= 4 && buffers <= 32 && quality >= 0 && quality <= 51 && bps >= 0, "Invalid encoder settings.");
        require(!path.empty(), "Output path is empty.");
        hr_check(static_cast<IUnknown*>(texture)->QueryInterface(IID_PPV_ARGS(&source)), "Query D3D11 texture");
        slots.resize(buffers);
    }
    ~session() { cleanup(); }
    void fail(const std::string& text) {
        std::lock_guard<std::mutex> lock(error_mutex);
        if (error.empty()) error = text;
        state = 4;
    }
    void nv_check(NVENCSTATUS result, const char* operation) {
        if (result == NV_ENC_SUCCESS) return;
        std::string detail;
        if (encoder && api.nvEncGetLastErrorString) {
            const char* value = api.nvEncGetLastErrorString(encoder);
            if (value) detail = value;
        }
        throw std::runtime_error(std::string(operation) + " NVENC=" + std::to_string(result) + " " + detail);
    }
    void init() {
        D3D11_TEXTURE2D_DESC desc{}; source->GetDesc(&desc);
        require(desc.Width == static_cast<UINT>(width) && desc.Height == static_cast<UINT>(height)
            && desc.SampleDesc.Count == 1 && desc.ArraySize == 1 && desc.MipLevels == 1,
            "Expected fixed-size, non-MSAA, single-mip RenderTexture.");
        DXGI_FORMAT input_format;
        switch (desc.Format) {
        case DXGI_FORMAT_R8G8B8A8_TYPELESS: case DXGI_FORMAT_R8G8B8A8_UNORM: case DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            input_format = DXGI_FORMAT_R8G8B8A8_UNORM; format = NV_ENC_BUFFER_FORMAT_ABGR; break;
        case DXGI_FORMAT_B8G8R8A8_TYPELESS: case DXGI_FORMAT_B8G8R8A8_UNORM: case DXGI_FORMAT_B8G8R8A8_UNORM_SRGB:
            input_format = DXGI_FORMAT_B8G8R8A8_UNORM; format = NV_ENC_BUFFER_FORMAT_ARGB; break;
        default: throw std::runtime_error("Expected RGBA8/BGRA8 RenderTexture. DXGI format=" + std::to_string(desc.Format));
        }
        source->GetDevice(&device); device->GetImmediateContext(&context);
        ComPtr<IDXGIDevice> dxgi; ComPtr<IDXGIAdapter> adapter; DXGI_ADAPTER_DESC adapter_desc{};
        hr_check(device.As(&dxgi), "Query DXGI device"); hr_check(dxgi->GetAdapter(&adapter), "Get adapter");
        hr_check(adapter->GetDesc(&adapter_desc), "Get adapter description");
        require(adapter_desc.VendorId == 0x10de, "Unity must render on an NVIDIA D3D11 GPU.");
        nv_module = LoadLibraryExW(L"nvEncodeAPI64.dll", nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
        require(nv_module != nullptr, "NVIDIA encoder driver DLL not found.");
        auto max_version = reinterpret_cast<NVENCSTATUS(NVENCAPI*)(uint32_t*)>(GetProcAddress(nv_module,"NvEncodeAPIGetMaxSupportedVersion"));
        auto create_api = reinterpret_cast<NVENCSTATUS(NVENCAPI*)(NV_ENCODE_API_FUNCTION_LIST*)>(GetProcAddress(nv_module,"NvEncodeAPICreateInstance"));
        require(max_version && create_api, "NVIDIA driver does not expose NVENC.");
        uint32_t supported = 0; nv_check(max_version(&supported), "Query API version"); driver_api = supported;
        require(supported >= ((NVENCAPI_MAJOR_VERSION << 4) | NVENCAPI_MINOR_VERSION),
            "NVENC API 13.0 required; driver reports " + std::to_string(supported >> 4) + "." + std::to_string(supported & 15));
        api.version = NV_ENCODE_API_FUNCTION_LIST_VER; nv_check(create_api(&api), "Create NVENC API");
        NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS open{}; open.version = NV_ENC_OPEN_ENCODE_SESSION_EX_PARAMS_VER;
        open.device = device.Get(); open.deviceType = NV_ENC_DEVICE_TYPE_DIRECTX; open.apiVersion = NVENCAPI_VERSION;
        nv_check(api.nvEncOpenEncodeSessionEx(&open, &encoder), "Open D3D11 encoder");
        uint32_t count = 0; nv_check(api.nvEncGetEncodeGUIDCount(encoder, &count), "Query codecs");
        std::vector<GUID> codecs(count); nv_check(api.nvEncGetEncodeGUIDs(encoder, codecs.data(), count, &count), "Get codecs");
        bool hevc = false; for (auto& codec : codecs) if (IsEqualGUID(codec, NV_ENC_CODEC_HEVC_GUID)) hevc = true;
        require(hevc, "This NVIDIA GPU does not support HEVC encoding.");
        NV_ENC_CAPS_PARAM caps{}; caps.version = NV_ENC_CAPS_PARAM_VER; caps.capsToQuery = NV_ENC_CAPS_ASYNC_ENCODE_SUPPORT;
        int async_supported = 0; nv_check(api.nvEncGetEncodeCaps(encoder,NV_ENC_CODEC_HEVC_GUID,&caps,&async_supported), "Query async support");
        require(async_supported != 0, "Asynchronous NVENC on Windows WDDM is required.");
        nv_check(api.nvEncGetInputFormatCount(encoder, NV_ENC_CODEC_HEVC_GUID, &count), "Query input formats");
        std::vector<NV_ENC_BUFFER_FORMAT> formats(count);
        nv_check(api.nvEncGetInputFormats(encoder,NV_ENC_CODEC_HEVC_GUID,formats.data(),count,&count), "Get input formats");
        bool format_supported = false; for (auto value : formats) if (value == format) format_supported = true;
        require(format_supported, "GPU does not support the requested RGB input format.");
        NV_ENC_PRESET_CONFIG preset{}; preset.version = NV_ENC_PRESET_CONFIG_VER; preset.presetCfg.version = NV_ENC_CONFIG_VER;
        nv_check(api.nvEncGetEncodePresetConfigEx(encoder, NV_ENC_CODEC_HEVC_GUID, NV_ENC_PRESET_P5_GUID,
            NV_ENC_TUNING_INFO_HIGH_QUALITY, &preset), "Get P5 preset");
        auto& cfg = preset.presetCfg;
        cfg.profileGUID = NV_ENC_HEVC_PROFILE_MAIN_GUID; cfg.gopLength = fps * 2; cfg.frameIntervalP = 1;
        cfg.rcParams.rateControlMode = NV_ENC_PARAMS_RC_VBR;
        cfg.rcParams.averageBitRate = bitrate; cfg.rcParams.maxBitRate = 0;
        cfg.rcParams.targetQuality = static_cast<uint8_t>(cq); cfg.rcParams.targetQualityLSB = 0;
        cfg.rcParams.enableLookahead = 0; cfg.rcParams.lookaheadDepth = 0;
        cfg.rcParams.enableAQ = 0; cfg.rcParams.enableTemporalAQ = 0;
        cfg.encodeCodecConfig.hevcConfig.idrPeriod = cfg.gopLength;
        cfg.encodeCodecConfig.hevcConfig.repeatSPSPPS = 1;
        cfg.encodeCodecConfig.hevcConfig.chromaFormatIDC = 1;
        auto& vui = cfg.encodeCodecConfig.hevcConfig.hevcVUIParameters;
        vui.videoSignalTypePresentFlag = 1; vui.videoFormat = static_cast<NV_ENC_VUI_VIDEO_FORMAT>(5); vui.videoFullRangeFlag = 0;
        vui.colourDescriptionPresentFlag = 1; vui.colourPrimaries = static_cast<NV_ENC_VUI_COLOR_PRIMARIES>(1);
        vui.transferCharacteristics = static_cast<NV_ENC_VUI_TRANSFER_CHARACTERISTIC>(1);
        vui.colourMatrix = static_cast<NV_ENC_VUI_MATRIX_COEFFS>(1);
        NV_ENC_INITIALIZE_PARAMS init{}; init.version = NV_ENC_INITIALIZE_PARAMS_VER;
        init.encodeGUID = NV_ENC_CODEC_HEVC_GUID; init.presetGUID = NV_ENC_PRESET_P5_GUID;
        init.encodeWidth = init.darWidth = init.maxEncodeWidth = width;
        init.encodeHeight = init.darHeight = init.maxEncodeHeight = height;
        init.frameRateNum = fps; init.frameRateDen = 1; init.enableEncodeAsync = 1; init.enablePTD = 1;
        init.tuningInfo = NV_ENC_TUNING_INFO_HIGH_QUALITY; init.encodeConfig = &cfg;
        nv_check(api.nvEncInitializeEncoder(encoder, &init), "Initialize HEVC encoder");
        D3D11_TEXTURE2D_DESC buffer_desc = desc;
        buffer_desc.Format = input_format; buffer_desc.Usage = D3D11_USAGE_DEFAULT;
        buffer_desc.BindFlags = D3D11_BIND_SHADER_RESOURCE; buffer_desc.CPUAccessFlags = 0; buffer_desc.MiscFlags = 0;
        for (auto& item : slots) {
            hr_check(device->CreateTexture2D(&buffer_desc, nullptr, &item.texture), "Create GPU encode buffer");
            D3D11_QUERY_DESC query_desc{D3D11_QUERY_EVENT, 0};
            hr_check(device->CreateQuery(&query_desc, &item.copy_done), "Create GPU completion query");
            NV_ENC_REGISTER_RESOURCE resource{}; resource.version = NV_ENC_REGISTER_RESOURCE_VER;
            resource.resourceType = NV_ENC_INPUT_RESOURCE_TYPE_DIRECTX; resource.resourceToRegister = item.texture.Get();
            resource.width = width; resource.height = height; resource.bufferFormat = format; resource.bufferUsage = NV_ENC_INPUT_IMAGE;
            nv_check(api.nvEncRegisterResource(encoder, &resource), "Register GPU input"); item.registered = resource.registeredResource;
            NV_ENC_CREATE_BITSTREAM_BUFFER output{}; output.version = NV_ENC_CREATE_BITSTREAM_BUFFER_VER;
            nv_check(api.nvEncCreateBitstreamBuffer(encoder, &output), "Create bitstream buffer"); item.output = output.bitstreamBuffer;
            item.done = CreateEventW(nullptr, FALSE, FALSE, nullptr); require(item.done != nullptr, "Create encode event failed.");
            NV_ENC_EVENT_PARAMS event{}; event.version = NV_ENC_EVENT_PARAMS_VER; event.completionEvent = item.done;
            nv_check(api.nvEncRegisterAsyncEvent(encoder, &event), "Register encode event"); item.event_registered = true;
        }
        eos_done = CreateEventW(nullptr, FALSE, FALSE, nullptr); require(eos_done != nullptr, "Create EOS event failed.");
        NV_ENC_EVENT_PARAMS eos_event{}; eos_event.version = NV_ENC_EVENT_PARAMS_VER; eos_event.completionEvent = eos_done;
        nv_check(api.nvEncRegisterAsyncEvent(encoder,&eos_event),"Register EOS event"); eos_registered = true;
        file = CreateFileW(path.c_str(), GENERIC_WRITE, FILE_SHARE_READ, nullptr, CREATE_NEW, FILE_ATTRIBUTE_NORMAL, nullptr);
        require(file != INVALID_HANDLE_VALUE, "Cannot create output file (existing files are never overwritten). Win32=" + std::to_string(GetLastError()));
        writer = std::thread([this] { write_loop(); }); state = 1;
    }
    void write_loop() {
        for (;;) {
            std::vector<uint8_t> data;
            { std::unique_lock<std::mutex> lock(writer_mutex); writer_cv.wait(lock,[this]{ return writer_stop || !packets.empty(); });
              if (packets.empty()) break;
              data = std::move(packets.front()); packets.pop_front(); queued_bytes -= data.size(); }
            DWORD actual = 0;
            if (!WriteFile(file, data.data(), static_cast<DWORD>(data.size()), &actual, nullptr) || actual != data.size()) {
                writer_failed = true; break;
            }
            bytes += actual; ++written;
        }
    }
    void collect() {
        while (!encoding.empty()) {
            auto& item = slots[encoding.front()];
            DWORD result = WaitForSingleObject(item.done, 0);
            if (result == WAIT_TIMEOUT) break;
            require(result == WAIT_OBJECT_0, "Encode completion event failed.");
            NV_ENC_LOCK_BITSTREAM output{}; output.version = NV_ENC_LOCK_BITSTREAM_VER;
            output.outputBitstream = item.output; output.doNotWait = 1;
            nv_check(api.nvEncLockBitstream(encoder, &output), "Lock completed HEVC packet");
            std::vector<uint8_t> packet(output.bitstreamSizeInBytes);
            std::memcpy(packet.data(), output.bitstreamBufferPtr, packet.size());
            nv_check(api.nvEncUnlockBitstream(encoder,item.output), "Unlock HEVC packet");
            nv_check(api.nvEncUnmapInputResource(encoder,item.mapped), "Unmap GPU input"); item.mapped = nullptr;
            { std::lock_guard<std::mutex> lock(writer_mutex);
              require(queued_bytes + packet.size() <= 64 * 1024 * 1024, "Compressed output queue full: storage is too slow.");
              queued_bytes += packet.size(); packets.push_back(std::move(packet)); }
            writer_cv.notify_one(); ++encoded; item.busy = false; encoding.pop_front();
        }
    }
    void submit_copies() {
        while (!copying.empty()) {
            int index = copying.front(); auto& item = slots[index];
            HRESULT result = context->GetData(item.copy_done.Get(), nullptr, 0, D3D11_ASYNC_GETDATA_DONOTFLUSH);
            if (result == S_FALSE) break;
            hr_check(result, "GPU copy completion");
            NV_ENC_MAP_INPUT_RESOURCE map{}; map.version = NV_ENC_MAP_INPUT_RESOURCE_VER; map.registeredResource = item.registered;
            nv_check(api.nvEncMapInputResource(encoder,&map), "Map GPU input"); item.mapped = map.mappedResource;
            NV_ENC_PIC_PARAMS pic{}; pic.version = NV_ENC_PIC_PARAMS_VER;
            pic.inputBuffer = item.mapped; pic.bufferFmt = format; pic.inputWidth = width; pic.inputHeight = height;
            pic.outputBitstream = item.output; pic.completionEvent = item.done; pic.pictureStruct = NV_ENC_PIC_STRUCT_FRAME;
            pic.inputTimeStamp = item.frame; pic.inputDuration = 1; pic.frameIdx = static_cast<uint32_t>(item.frame);
            NVENCSTATUS result_nv = api.nvEncEncodePicture(encoder,&pic);
            if (result_nv != NV_ENC_ERR_NEED_MORE_INPUT) nv_check(result_nv, "Submit HEVC frame");
            encoding.push_back(index); copying.pop_front(); ++submitted;
        }
    }
    void pump() {
        require(!writer_failed, "Could not write compressed HEVC output.");
        collect(); submit_copies(); collect();
    }
    void capture() {
        if (state == 0) init();
        if (state != 1) return;
        pump();
        auto free_slot = [this]() { for (int i=0;i<static_cast<int>(slots.size());++i) if (!slots[i].busy) return i; return -1; };
        int index = free_slot();
        if (index < 0) {
            ++pressure; auto deadline = clock_type::now() + std::chrono::seconds(5);
            while ((index = free_slot()) < 0) {
                pump(); require(clock_type::now() < deadline, "GPU encoder timed out waiting for a free buffer."); Sleep(1);
            }
        }
        auto& item = slots[index]; item.busy = true; item.frame = captured++;
        context->CopyResource(item.texture.Get(), source.Get());
        context->End(item.copy_done.Get()); context->Flush(); ++copies; copying.push_back(index);
        pump();
    }
    void close() {
        if (state == 3) return;
        bool failed = state == 4; if (!failed) state = 2;
        try {
            if (encoder && !failed) {
                auto deadline = clock_type::now() + std::chrono::seconds(10);
                while (!copying.empty()) { pump(); require(clock_type::now() < deadline,"GPU copy drain timed out."); Sleep(1); }
                NV_ENC_PIC_PARAMS eos{}; eos.version = NV_ENC_PIC_PARAMS_VER; eos.encodePicFlags = NV_ENC_PIC_FLAG_EOS;
                eos.completionEvent = eos_done;
                nv_check(api.nvEncEncodePicture(encoder,&eos), "Flush NVENC");
                while (!encoding.empty()) { collect(); require(clock_type::now() < deadline,"NVENC drain timed out."); Sleep(1); }
                while (WaitForSingleObject(eos_done,0) == WAIT_TIMEOUT) {
                    require(clock_type::now() < deadline,"NVENC EOS completion timed out."); Sleep(1);
                }
            }
        } catch (const std::exception& e) { fail(e.what()); }
        cleanup();
        if (writer_failed) fail("Could not write all compressed HEVC packets.");
        if (state != 4) state = 3;
        finished = true;
    }
    void cleanup() noexcept {
        { std::lock_guard<std::mutex> lock(writer_mutex); writer_stop = true; }
        writer_cv.notify_all(); if (writer.joinable()) writer.join();
        if (file != INVALID_HANDLE_VALUE) { FlushFileBuffers(file); CloseHandle(file); file = INVALID_HANDLE_VALUE; }
        if (encoder) {
            if (eos_registered) { NV_ENC_EVENT_PARAMS event{}; event.version = NV_ENC_EVENT_PARAMS_VER;
                event.completionEvent = eos_done; api.nvEncUnregisterAsyncEvent(encoder,&event); eos_registered = false; }
            for (auto& item : slots) {
                if (item.mapped) api.nvEncUnmapInputResource(encoder,item.mapped);
                if (item.event_registered) { NV_ENC_EVENT_PARAMS event{}; event.version = NV_ENC_EVENT_PARAMS_VER;
                    event.completionEvent = item.done; api.nvEncUnregisterAsyncEvent(encoder,&event); }
                if (item.output) api.nvEncDestroyBitstreamBuffer(encoder,item.output);
                if (item.registered) api.nvEncUnregisterResource(encoder,item.registered);
                item.mapped = nullptr; item.output = nullptr; item.registered = nullptr; item.event_registered = false;
            }
            api.nvEncDestroyEncoder(encoder); encoder = nullptr;
        }
        for (auto& item : slots) { if (item.done) CloseHandle(item.done); item.done = nullptr; item.texture.Reset(); item.copy_done.Reset(); }
        if (eos_done) { CloseHandle(eos_done); eos_done = nullptr; }
        source.Reset(); context.Reset(); device.Reset();
        if (nv_module) { FreeLibrary(nv_module); nv_module = nullptr; }
    }
};
static std::mutex registry_mutex;
static std::unordered_map<int,std::shared_ptr<session>> sessions;
static std::atomic<int> next_id{1};
static std::shared_ptr<session> find_session(int id) {
    std::lock_guard<std::mutex> lock(registry_mutex); auto found = sessions.find(id);
    return found == sessions.end() ? nullptr : found->second;
}
static void copy_text(char* target, int capacity, const std::string& value) {
    if (target && capacity > 0) { size_t length = (std::min)(value.size(),static_cast<size_t>(capacity-1));
        std::memcpy(target,value.data(),length); target[length] = 0; }
}
API int __cdecl ng_create(void* texture, int width, int height, int fps, int bitrate, int cq,
                         const wchar_t* path, int buffers, char* error, int capacity) {
    try {
        auto value = std::make_shared<session>(texture,width,height,fps,bitrate,cq,path,buffers);
        int id = next_id++; std::lock_guard<std::mutex> lock(registry_mutex); sessions.emplace(id,std::move(value)); return id;
    } catch (const std::exception& e) { copy_text(error,capacity,e.what()); return 0; }
}
// UnityRenderingEventAndData: 1=capture, 2=pump, 3=drain/close.
static void __stdcall render_event(int operation, void* data) noexcept {
    auto value = find_session(static_cast<int>(reinterpret_cast<intptr_t>(data))); if (!value) return;
    try {
        if (operation == 3) value->close();
        else if (operation == 1) value->capture();
        else if (operation == 2 && value->state == 1) value->pump();
    } catch (const std::exception& e) { value->fail(e.what()); }
    catch (...) { value->fail("Unexpected native exception."); }
}
API void* __cdecl ng_render_event() { return reinterpret_cast<void*>(&render_event); }
API int __cdecl ng_status(int id, gpu_stats* stats, char* error, int capacity) {
    auto value = find_session(id); if (!value || !stats) return -1;
    *stats = {value->state.load(),value->driver_api.load(),value->width,value->height,
        value->captured.load(),value->submitted.load(),value->encoded.load(),value->written.load(),value->bytes.load(),
        value->pressure.load(),value->copies.load(),0};
    std::lock_guard<std::mutex> lock(value->error_mutex); copy_text(error,capacity,value->error); return stats->state;
}
API int __cdecl ng_forget(int id) {
    std::lock_guard<std::mutex> lock(registry_mutex); auto found = sessions.find(id);
    if (found == sessions.end()) return 0;
    // A failed session must also receive operation 3 before it can be forgotten.
    if (!found->second->finished) return 0;
    sessions.erase(found); return 1;
}
