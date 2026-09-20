#include "nvenc_gpu.cpp"
#include <cstdio>
int wmain(int argc, wchar_t** argv) {
    if (argc < 2) return 2;
    try {
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
        hr_check(D3D11CreateDevice(nullptr,D3D_DRIVER_TYPE_HARDWARE,nullptr,0,nullptr,0,D3D11_SDK_VERSION,&device,nullptr,&context),"Create smoke device");
        D3D11_TEXTURE2D_DESC desc{}; desc.Width = 640; desc.Height = 360; desc.MipLevels = desc.ArraySize = 1;
        desc.Format = DXGI_FORMAT_R8G8B8A8_UNORM; desc.SampleDesc.Count = 1;
        desc.Usage = D3D11_USAGE_DEFAULT; desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> texture; ComPtr<ID3D11RenderTargetView> target;
        hr_check(device->CreateTexture2D(&desc,nullptr,&texture),"Create texture");
        hr_check(device->CreateRenderTargetView(texture.Get(),nullptr,&target),"Create RTV");
        char error[2048]{}; int id = ng_create(texture.Get(),640,360,60,4000000,0,argv[1],8,error,sizeof(error));
        require(id > 0,error);
        auto data = reinterpret_cast<void*>(static_cast<intptr_t>(id));
        gpu_stats stats{};
        for(int frame=0;frame<180;++frame) {
            float color[4] = {0,0,0,1}; color[frame/60] = 1;
            context->ClearRenderTargetView(target.Get(),color);
            render_event(1,data);
            ng_status(id,&stats,error,sizeof(error)); require(stats.state != 4,error);
        }
        render_event(3,data); ng_status(id,&stats,error,sizeof(error));
        std::printf("{\"state\":%d,\"captured\":%lld,\"submitted\":%lld,\"encoded\":%lld,\"written\":%lld,\"bytes\":%lld,\"backpressure\":%lld,\"gpu_copies\":%lld,\"raw_readback_bytes\":%lld,\"api\":%d}\n",
            stats.state,stats.captured,stats.submitted,stats.encoded,stats.written,stats.bytes,stats.backpressure,stats.gpu_copies,stats.raw_readback_bytes,stats.api_version);
        require(stats.state == 3 && stats.written == 180,error);
        require(ng_forget(id) == 1,"Session not released");
        return 0;
    } catch(const std::exception& e) { std::fprintf(stderr,"%s\n",e.what()); return 1; }
}
