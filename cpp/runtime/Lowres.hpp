#pragma once
// Handhelds load a pre-scaled art set instead of the 4x desktop set (see tools/BakeSvgs --psp / --vita).
#if defined(__PSP__)
#define MG_LOWRES 1
#define MG_LOWRES_SUFFIX ".psp.png"
#define MG_LOWRES_SIZES "psp_sizes.json"
#define MG_MAX_TEX 512            // PSP GU texture limit
#define MG_NO_RT 1                // no render-target textures: scale with SDL logical size instead
#elif defined(__vita__)
#define MG_LOWRES 1
#define MG_LOWRES_SUFFIX ".vita.png"
#define MG_LOWRES_SIZES "vita_sizes.json"
#define MG_MAX_TEX 1024           // keeps the biggest backdrops at a sane size in 512 MB of RAM
#define MG_NO_RT 1
#endif
