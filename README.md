# Appx Re-Sign
A small tool to help automate resigning Appx Packages with the help of MS tooling
(Source and Release will be uploaded soon)

### Usage
`Appx Re-Sign.exe -a "Path to appx package" -p Publisher -o "Path to output folder"`


### Note
This tool uses utilities from Windows SDK (All rights to these are reserved to Microsoft):
- `makeappx.exe`
- `makecert.exe`
- `signtool.exe`
- `pvk2pfx.exe`
