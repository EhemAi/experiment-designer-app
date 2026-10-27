# Third-party notices

The original Experiment Designer source is licensed under MIT. Separately
licensed assets retain their own licenses; an affiliation does not imply
university endorsement.

| Component | Use | License and notices |
| --- | --- | --- |
| Manrope Regular/Bold | Privately loaded application fonts | SIL OFL 1.1; full notice in `src/PoultryDesigner.Desktop/Assets/Fonts/Manrope-OFL.txt` |
| Poppins Regular/SemiBold | Privately loaded application fonts | SIL OFL 1.1; full notice in `src/PoultryDesigner.Desktop/Assets/Fonts/Poppins-OFL.txt` |
| Microsoft .NET / Windows Desktop runtime | Windows release runtime | Microsoft distribution's `Runtime/LICENSE.txt` and `Runtime/ThirdPartyNotices.txt`, shipped unchanged in each ZIP |

The application, installer and test projects have no third-party NuGet package
references. The .NET SDK and framework reference packs are build prerequisites,
not source-vendored dependencies. Their versions are controlled by `global.json`
and `runtime-version.txt`; empty NuGet lockfiles document the project graph.

Font files may be redistributed with their OFL notices, separately from the MIT
source license. Do not sell the font files by themselves or imply that the font
authors endorse this software. Preserve Microsoft and upstream notices when
redistributing the Windows runtime. The original macro workbook, private study
data, development tools and promotional video dependencies are not part of the
open-source release.
