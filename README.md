# Package Re-Sign

A tool to help automate re-(packaging/signing) (and optionally modifying) Appx/Msix Packages with the help of MS tooling

### What's New?

- Support msix, appx and bundles
- Output will be similar to input
- Publisher will be fetched from the original package (by default)
- User can choose custom PFX files
- No more password dialogs
- It will generate installer (`.bat`) that will install the cert and launch the package
- Easy to use with few default configs
- Works standalone (just double click to open) or with cmd
- Deps will be copied to the new signed package
- User can define more files/folders to copy to the new signed package
- Detailed handling for errors with option to retry the failed process


## How to use?

### Standalone (Recommended)

Double click on `pkgrsn.exe` then follow the instructions


### Configuration

**Config file:** `pkgrsn.exe.Config`


**Option:** `debugOutput`

Debug output state [0: off, 1:on]


**Option:** `modifyByDefault`

Allow package modification by default [0: off, 1:on]


**Option:** `defaultPublisher`

This will be used only if getting original publisher failed


**Option:** `defaultPFXPassword`

default pfx password for (exist or new) certificate [any password you choose]

**IMPORTANT:** when you need to share this app be sure to remove your saved password from config file!

    
**Option:** `foldersToCopy`

Folders to be copied to the output package path (only name not full path)
   

**Option:** `filesToCopy`

Files to be copied to the output package path (only name not full path)




### Using command (Optional)

Note that it's preferred to use this app standalone

```
pkgrsn -a "Path to package" --skip [--modify]


  -a, --app-package      Required. The input package to be re-signed

  -p, --publisher        Required. The name of the publisher (Must match the AppxManifest.xml publisher).

  -o, --output-folder    Required. The desired output folder for the signed package
  
  -x, --pfx              PFX file for package signing
  
  -s, --password         PFX password

  -m, --modify           Allow package modifications
						 
  -k, --skip             Apply default config for quick re-sign
						 
  --help                 Display this help screen.

  --version              Display version information.
```


### Note

This tool uses utilities from Windows SDK (All rights to these are reserved to Microsoft):

- `makeappx.exe`
- `makecert.exe`
- `signtool.exe`
- `pvk2pfx.exe`
- `powershell`
- `cmd`