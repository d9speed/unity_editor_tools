# Third-party components

`native~/include/nvEncodeAPI.h` is the NVIDIA NVENC API 13.0 header, obtained from
FFmpeg/nv-codec-headers tag `n13.0.19.0`:

https://github.com/FFmpeg/nv-codec-headers/blob/n13.0.19.0/include/ffnvcodec/nvEncodeAPI.h

Copyright (c) 2010-2024 NVIDIA Corporation. The header's MIT-style permission and
copyright notice are retained verbatim in the file. No NVIDIA driver DLL is bundled.

The application invokes a separately installed FFmpeg executable for lossless HEVC
remuxing, ProRes encoding, capability checks, and PNG extraction. FFmpeg is not linked
into the native plugin and is not redistributed in this package. Users obtain their
own FFmpeg build and follow its license terms.

The native DLL is built from `native~/nvenc_gpu.cpp` and the above header. Windows
system libraries and the NVIDIA driver provide the graphics/encoding APIs at runtime.

API references:

- https://docs.nvidia.com/video-technologies/video-codec-sdk/13.0/nvenc-video-encoder-api-prog-guide/
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Texture.GetNativeTexturePtr.html
- https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Rendering.CommandBuffer.IssuePluginEventAndData.html
