# VL.Devices.Axis

Support for [network cameras](https://www.axis.com/products/network-cameras) by [Axis](https://www.axis.com/).

[VAPIX®](https://developer.axis.com/vapix/), the API's for cameras by Axis are vast. This NuGet only wraps a fraction of those for convenient use. If you need support for a specific API, it can most likely be added without too much effort. 

Currently only supports choosing a streams resolution, fps and toggle the IRCut filter.

For use with vvvv, the visual live-programming environment for .NET: http://vvvv.org

## Getting started
- Install as [described here](https://thegraybook.vvvv.org/reference/hde/managing-nugets.html) via commandline:

    `nuget install VL.Devices.Axis -pre -source nuget.org -source https://f.feedz.io/videolan/preview/nuget/index.json`

- Usage examples and more information are included in the pack and can be found via the [Help Browser](https://thegraybook.vvvv.org/reference/hde/findinghelp.html)

## Contributing
- Report issues on [the vvvv forum](https://forum.vvvv.org/c/vvvv-gamma/28)
- For custom development requests, please [get in touch](mailto:devvvvs@vvvv.org)
- When making a pull-request, please make sure to read the general [guidelines on contributing to vvvv libraries](https://thegraybook.vvvv.org/reference/extending/contributing.html)

## For Developers
When running as source package, make sure the following packages are installed
- `nuget install VideoLAN.LibVLC.Windows -pre -version 4.0.0-alpha-20250220 -source https://f.feedz.io/videolan/preview/nuget/index.json`
- `nuget install LibVLCSharp -pre -version 4.0.0-alpha-20250220-8602 -source https://f.feedz.io/videolan/preview/nuget/index.json`

## Sponsoring
Development of this library was partially sponsored by:  
* [Refik Anadol Studio](https://refikanadolstudio.com/)
