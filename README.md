# Appx Re-Sign
A small tool to help automate resigning (and optionally modifying) Appx Packages with the help of MS tooling

### What's Updated?
- Added chance to pause the process and open the application's working folder to make changes to the App Packages being signed (the `-m` switch).
- Added clearing the temp folder when task successful (previously was only on app start)
- Switched to [CommandLineParser](https://github.com/commandlineparser/commandline) for handling the inputted switches

### Usage
```
"Appx Re-Sign.exe" -a "Path to appx package" -p Publisher -o "Path to output folder"


  -a, --app-package      Required. The input Appx/Appxbundle package to be re-signed

  -p, --publisher        Required. The name of the publisher (Must match the AppxManifest.xml publisher). If the
                         publisher is formatted like:
                         "CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"
                         then input into this app with quotes:
                         "Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US"

  -o, --output-folder    Required. The desired output folder for the signed Appxbundle

  -m, --modify           (Optional) Use this switch to pause the process to allow modifications to the package before
                         re-signing

  --help                 Display this help screen.

  --version              Display version information.
```


### Note
This tool uses utilities from Windows SDK (All rights to these are reserved to Microsoft):
- `makeappx.exe`
- `makecert.exe`
- `signtool.exe`
- `pvk2pfx.exe`

- A very small amount of Appx files *might* cause issues when being bundled into an AppxBundle, this will be looked at next update.
