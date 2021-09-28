# SendsafelyAPI
An optimized fork of [SendSafely's API for .NET](https://github.com/SendSafely/Windows-Client-API).

## Modifications
### Downloading
- Cached numerous operations
- Preallocate memory streams instead of file stream strategy
- Download all segments in parallel
- Optimized decryption process
### Uploading
- Preallocate memory streams instead of file stream strategy
- Store prebuilt segments into streams
- Parallelize encryption and sending of segments
### Misc
- Targets .NET 5
- Moves references to Nuget
- Adjusted progress reporting based on the number of parts sent.
- Little fixes as found
