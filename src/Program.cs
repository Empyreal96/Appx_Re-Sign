using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Appx_Re_sign
{
    class Program
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        static string currentDir = Environment.CurrentDirectory;
        static void Main(string[] args)
        {
            try
            {
                Console.WriteLine("Appx Re-Sign tool by Empyreal96");
                Console.WriteLine("\n");
                Task.Delay(2000);
                if (args.Length != 0)
                {
                    if (args[0] != "-a")
                    {
                        ShowUsage();
                        return;
                    }
                    else
                    {

                    }
                    if (args[2] != "-p")
                    {
                        ShowUsage();
                        return;

                    }
                    if (args[4] != "-o")
                    {
                        ShowUsage();
                        return;
                    }

                    if (!Directory.Exists(".\\Temp"))
                    {
                        Directory.CreateDirectory(".\\Temp");
                    }
                    else
                    {
                        Directory.Delete(".\\Temp", true);
                        Directory.CreateDirectory(".\\Temp");
                    }

                    if (!args[1].Contains("appx"))
                    {
                        if (!args[1].Contains("appxbundle"))
                        {
                            Console.WriteLine("Please select an Appx/Appxbundle\n");
                            ShowUsage();
                            return;
                            /* if (!args[1].Contains("msix"))
                             {
                                 Console.WriteLine("Please select an Appx/Appxbundle/Msix file\n");
                                 ShowUsage();
                                 return;
                             } */
                        }

                    }

                    string APPX_PATH = args[1];
                    string PUBLISHER = args[3];
                    string OUTPUT_PATH = args[5];
                    if (File.Exists(APPX_PATH))
                    {
                        Console.WriteLine($"# Selected App Package: {APPX_PATH}");
                    }
                    else
                    {
                        Console.WriteLine("App Package not found!\n\n");
                    }
                    Console.WriteLine($"# Publisher Name: {PUBLISHER}");

                    if (OUTPUT_PATH.EndsWith("\\"))
                    {
                        OUTPUT_PATH = OUTPUT_PATH.Remove(OUTPUT_PATH.Length - 1);
                    }

                    if (!Directory.Exists(OUTPUT_PATH))
                    {
                        Directory.CreateDirectory(OUTPUT_PATH);
                    }
                    string PackageName = Path.GetFileNameWithoutExtension(APPX_PATH);
                    string PackageNameWithEx = Path.GetFileName(APPX_PATH);

                    if (File.Exists($"{OUTPUT_PATH}\\{PackageName}.appxbundle"))
                    {
                        Random random = new Random();
                        string randChar = new string(Enumerable.Repeat(chars, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                        PackageName = $"{PackageName}_{randChar}";

                    }
                    Console.WriteLine($"# Output: {OUTPUT_PATH}");

                   

                    using (var zip = ZipFile.OpenRead(APPX_PATH))
                    {
                        Console.WriteLine("\n[Extracting App Package to sign]");
                        zip.ExtractToDirectory($".\\Temp\\{PackageName}");
                        Directory.CreateDirectory(".\\Temp\\Staging");
                    }

                    if (!PackageNameWithEx.ToLower().Contains("appxbundle"))
                    {
                        
                    
                    Console.WriteLine("\n[Packing App]\n");
                    var makeappx = new Process();
                    makeappx.StartInfo.FileName = ".\\tools\\makeappx.exe";
                    makeappx.StartInfo.Arguments = $"pack /nv /v /h SHA256 /d \".\\Temp\\{PackageName}\" /p \".\\Temp\\Staging\\{PackageNameWithEx}\"";
                    makeappx.StartInfo.RedirectStandardOutput = true;
                    makeappx.StartInfo.RedirectStandardError = true;
                    makeappx.StartInfo.UseShellExecute = false;
                    makeappx.Start();

                    StreamReader makeappxReader = makeappx.StandardOutput;
                    string makeappxOutput = makeappxReader.ReadToEnd();
                    Console.WriteLine(makeappxOutput + "\n");
                    makeappx.WaitForExit();
                    // var exitCode = makeappx.ExitCode;
                    makeappx.Close();
                    }

                    Console.WriteLine("[Making new certificate]\n");
                    var makecert = new Process();
                    makecert.StartInfo.FileName = ".\\tools\\makecert.exe";
                    makecert.StartInfo.Arguments = $"-r -h 0 -n \"CN={PUBLISHER}\" -pe -sv \".\\Temp\\{PackageName}.pvk\" \".\\Temp\\{PackageName}.cer\"";
                    makecert.StartInfo.RedirectStandardOutput = true;
                    makecert.StartInfo.RedirectStandardError = true;
                    makecert.StartInfo.UseShellExecute = false;
                    makecert.Start();

                    StreamReader makecertReader = makecert.StandardOutput;
                    string makecertOutput = makecertReader.ReadToEnd();
                    Console.WriteLine(makecertOutput + "\n");
                    makecert.WaitForExit();
                    makecert.Close();


                    Console.WriteLine("[Creating code signing certificate (PFX)]\n");
                    Process pvk2pfx = new Process();
                    pvk2pfx.StartInfo.FileName = ".\\tools\\pvk2pfx.exe";
                    pvk2pfx.StartInfo.Arguments = $"-pvk \".\\Temp\\{PackageName}.pvk\" -spc \".\\Temp\\{PackageName}.cer\" -pfx \".\\Temp\\{PackageName}.pfx\"";
                    pvk2pfx.StartInfo.RedirectStandardOutput = true;
                    pvk2pfx.StartInfo.RedirectStandardError = true;
                    pvk2pfx.StartInfo.UseShellExecute = false;
                    pvk2pfx.Start();

                    StreamReader pvk2pfxReader = pvk2pfx.StandardOutput;
                    string pvk2pfxOutput = pvk2pfxReader.ReadToEnd();
                    Console.WriteLine(pvk2pfxOutput + "\n");
                    pvk2pfx.WaitForExit();
                    pvk2pfx.Close();

                    if (!PackageNameWithEx.ToLower().Contains("appxbundle"))
                    {

                        Console.WriteLine("[Signing Package (Part 1 of 2)]\n");
                        Process signtool = new Process();
                        signtool.StartInfo.FileName = ".\\tools\\signtool.exe";
                        signtool.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \".\\Temp\\Staging\\{PackageNameWithEx}\"";
                        signtool.StartInfo.RedirectStandardOutput = true;
                        signtool.StartInfo.RedirectStandardError = true;
                        signtool.StartInfo.UseShellExecute = false;
                        signtool.Start();

                        StreamReader signtoolReader = signtool.StandardOutput;
                        //StreamReader signtoolReader = signtool.StandardError;

                        string signtoolOutput = signtoolReader.ReadToEnd();
                        if (signtoolOutput.Contains("0x8007000b"))
                        {
                            Console.WriteLine("Error signing package, make sure Publisher matches the AppxManifest Publisher");
                            signtool.WaitForExit();
                            signtool.Close();
                            return;
                        }
                        Console.WriteLine(signtoolOutput + "\n");
                        signtool.WaitForExit();
                        signtool.Close();
                    } else
                    {
                        int pkgInt = 0;
                        var bundleFiles = Directory.EnumerateFiles($".\\Temp\\{PackageName}");
                        foreach (var file in bundleFiles)
                        {
                            if (file.ToLower().Contains(".appx"))
                            {
                                
                                string pkgname = Path.GetFileName(file);
                                File.Copy(file, $@"{currentDir}\Temp\Staging\{pkgname}");
                                pkgInt++;
                                Console.WriteLine($"[Signing Package {pkgInt} (Part 1 of 2)]\n");
                                Process signtool = new Process();
                                signtool.StartInfo.FileName = ".\\tools\\signtool.exe";
                                signtool.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \".\\Temp\\Staging\\{pkgname}\"";
                                signtool.StartInfo.RedirectStandardOutput = true;
                                signtool.StartInfo.RedirectStandardError = true;
                                signtool.StartInfo.UseShellExecute = false;
                                signtool.Start();

                                StreamReader signtoolReader = signtool.StandardOutput;
                                //StreamReader signtoolReader = signtool.StandardError;

                                string signtoolOutput = signtoolReader.ReadToEnd();
                                if (signtoolOutput.Contains("0x8007000b"))
                                {
                                    Console.WriteLine("Error signing package, make sure Publisher matches the AppxManifest Publisher");
                                    signtool.WaitForExit();
                                    signtool.Close();
                                    return;
                                }
                                Console.WriteLine(signtoolOutput + "\n");
                                signtool.WaitForExit();
                                signtool.Close();
                            }
                        }
                    }

                    Console.WriteLine("[Creating final appxbundle]\n");
                    Process makeappxbundle = new Process();
                    makeappxbundle.StartInfo.FileName = ".\\tools\\makeappx.exe";
                    makeappxbundle.StartInfo.Arguments = $"bundle /d \".\\Temp\\Staging\" /p \"{OUTPUT_PATH}\\{PackageName}.appxbundle\"";
                    makeappxbundle.StartInfo.RedirectStandardOutput = true;
                    makeappxbundle.StartInfo.RedirectStandardError = true;
                    makeappxbundle.StartInfo.UseShellExecute = false;
                    makeappxbundle.Start();

                    StreamReader makeappxbundleReader = makeappxbundle.StandardOutput;
                    string makeappxbundleOutput = makeappxbundleReader.ReadToEnd();
                    Console.WriteLine(makeappxbundleOutput + "\n");
                    makeappxbundle.WaitForExit();
                    makeappxbundle.Close();


                    Console.WriteLine("[Signing Package (Part 2 of 2)]\n");
                    Process signtool2 = new Process();
                    signtool2.StartInfo.FileName = ".\\tools\\signtool.exe";
                    signtool2.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \"{OUTPUT_PATH}\\{PackageName}.appxbundle\"";
                    signtool2.StartInfo.RedirectStandardOutput = true;
                    signtool2.StartInfo.RedirectStandardError = true;
                    signtool2.StartInfo.UseShellExecute = false;
                    signtool2.Start();

                    StreamReader signtool2Reader = signtool2.StandardOutput;
                    string signtool2Output = signtool2Reader.ReadToEnd();
                    if (signtool2Output.Contains("0x8007000b"))
                    {
                        Console.WriteLine("Error signing package, make sure Publisher matches the AppxManifest Publisher");
                        signtool2.WaitForExit();
                        signtool2.Close();
                        return;
                    }
                    Console.WriteLine(signtool2Output + "\n");
                    signtool2.WaitForExit();
                    signtool2.Close();

                    
                    //Console.WriteLine($"\"{currentDir}\\Temp\\{PackageName}.pvk\" " + $"\"{OUTPUT_PATH}\\{PackageName}.pvk\"");
                    File.Copy($@"{currentDir}\Temp\{PackageName}.pvk", $@"{OUTPUT_PATH}\{PackageName}_PVK.pvk");
                    File.Copy($@"{currentDir}\Temp\{PackageName}.pfx", $@"{OUTPUT_PATH}\{PackageName}_PFX.pfx");
                    File.Copy($@"{currentDir}\Temp\{PackageName}.cer", $@"{OUTPUT_PATH}\{PackageName}_CERT.cer");

                    Console.WriteLine("Package packed and signed successfully, Opening output folder");
                    Process.Start($"\"{OUTPUT_PATH}\"");
                }
                else
                {
                    ShowUsage();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}\n\n{ex.StackTrace}\n\n{ex.Source}");
            }
        }

        static public void ShowUsage()
        {

            Console.WriteLine("AppxRePack.exe -a \"Path to Appx(bundle) file\" -p \"Publisher\" -o \"Output folder\" ");

        }
    }
}
