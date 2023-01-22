using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommandLine;

namespace Appx_Re_sign
{
    class Program
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        static string currentDir = Environment.CurrentDirectory;
        public class CommandOptions
        {
            [Option('a', "app-package", Required = true, HelpText = "The input Appx/Appxbundle package to be re-signed")]
            public string APPX_PATH { get; set; }

            [Option('p', "publisher", Required = true, HelpText = "The name of the publisher (Must match the AppxManifest.xml publisher). " +
                "If the publisher is formatted like: \n\"CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\" \nthen input into this app with quotes:\n" +
                "\"Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\"")]
            public string PUBLISHER { get; set; }

            [Option('o', "output-folder", Required = true, HelpText = "The desired output folder for the signed Appxbundle")]
            public string OUTPUT_PATH { get; set; }

            [Option('m', "modify", Required = false, HelpText = "(Optional) Use this switch to pause the process to allow modifications to the package before re-signing")]
            public bool IsModify { get; set; }

            
           
        }

        static void Main(string[] args)
        {
            try
            {
                

                Parser.Default.ParseArguments<CommandOptions>(args)
                    .WithParsed<CommandOptions>(o =>
                    {
                        Console.WriteLine("\n\nAppx Re-Sign tool by Empyreal96");
                        Console.WriteLine("\n");
                        Task.Delay(2000);
                        if (!o.APPX_PATH.Contains("appx"))
                        {
                            if (!o.APPX_PATH.Contains("appxbundle"))
                            {
                                Console.WriteLine($"\"{o.APPX_PATH}\" is not an appx/appxbundle");
                                
                                return;
                            }

                        }

                        ClearTempFiles();

                        if (File.Exists(o.APPX_PATH))
                        {
                            Console.WriteLine($"# Selected App Package: {o.APPX_PATH}");
                        }
                        else
                        {
                            Console.WriteLine("App Package not found!\n\n");
                        }
                        Console.WriteLine($"# Publisher Name: {o.PUBLISHER}");


                        if (o.OUTPUT_PATH.EndsWith("\\"))
                        {
                            o.OUTPUT_PATH = o.OUTPUT_PATH.Remove(o.OUTPUT_PATH.Length - 1);
                        }

                        if (!Directory.Exists(o.OUTPUT_PATH))
                        {
                            Directory.CreateDirectory(o.OUTPUT_PATH);
                        }
                        string PackageName = Path.GetFileNameWithoutExtension(o.APPX_PATH);
                        string PackageNameWithEx = Path.GetFileName(o.APPX_PATH);

                        if (File.Exists($"{o.OUTPUT_PATH}\\{PackageName}.appxbundle"))
                        {
                            Random random = new Random();
                            string randChar = new string(Enumerable.Repeat(chars, 5).Select(s => s[random.Next(s.Length)]).ToArray());
                            PackageName = $"{PackageName}_{randChar}";

                        }
                        Console.WriteLine($"# Output: {o.OUTPUT_PATH}\\{PackageName}.appxbundle");



                        using (var zip = ZipFile.OpenRead(o.APPX_PATH))
                        {
                            Console.WriteLine("\n[Extracting App Package to sign]");
                            zip.ExtractToDirectory($".\\Temp\\{PackageName}");
                            Directory.CreateDirectory(".\\Temp\\Staging");
                            if (PackageNameWithEx.ToLower().Contains("appxbundle"))
                            {
                                if (o.IsModify == true)
                                {
                                    var appxbundleFiles = Directory.EnumerateFiles($".\\Temp\\{PackageName}");
                                    foreach (var file in appxbundleFiles)
                                    {
                                        if (file.ToLower().Contains(".appx"))
                                        {
                                            using (var zip1 = ZipFile.OpenRead(file))
                                            {
                                                string appxName = Path.GetFileNameWithoutExtension(file);
                                                Directory.CreateDirectory($".\\Temp\\{PackageName}\\Individual\\{appxName}");
                                                zip1.ExtractToDirectory($".\\Temp\\{PackageName}\\Individual\\{appxName}");
                                            }
                                        }
                                    }
                                }

                            }
                        }

                        if (o.IsModify == true)
                        {
                            Console.WriteLine("\n[Opening directory to modify files]\n");
                            if (PackageNameWithEx.ToLower().Contains("appxbundle"))
                            {
                                Process.Start($".\\Temp\\{PackageName}\\Individual\\");
                            }
                            else
                            {
                                Process.Start($".\\Temp\\{PackageName}");
                            }
                            Console.WriteLine("[After making changes, press any key to continue]\n");
                            Console.ReadLine();
                        }

                        if (!PackageNameWithEx.ToLower().Contains("appxbundle"))
                        {
                            Console.WriteLine("\n[Packing App]\n");
                            PackLooseFiles($".\\Temp\\{PackageName}", $".\\Temp\\Staging\\{PackageNameWithEx}");
                        }
                        else
                        {
                            if (o.IsModify == true)
                            {

                                Console.WriteLine("\n[Packing Appx files for bundle]\n");
                                var individualAppx = Directory.EnumerateDirectories($".\\Temp\\{PackageName}\\Individual\\");
                                Directory.CreateDirectory($".\\Temp\\{PackageName}\\Individual\\files");
                                foreach (var appxDir in individualAppx)
                                {
                                    Console.WriteLine($"\nPacking: {appxDir}\n");
                                    //Console.ReadLine();
                                    string individualAppxName = Path.GetFileName(appxDir).Replace(".appx", "");

                                    PackLooseFiles(appxDir, $".\\Temp\\{PackageName}\\Individual\\files\\{individualAppxName}.appx");
                                    //Console.WriteLine($"DEBUG NOTE: \".\\Temp\\{PackageName}\\Individual\\files\\{individualAppxName}.appx\"");
                                }
                                //Console.ReadLine();
                            }
                        }



                        Console.WriteLine("[Making new certificate]\n");
                        var makecert = new Process();
                        makecert.StartInfo.FileName = ".\\tools\\makecert.exe";
                        makecert.StartInfo.Arguments = $"-r -h 0 -n \"CN={o.PUBLISHER}\" -pe -sv \".\\Temp\\{PackageName}.pvk\" \".\\Temp\\{PackageName}.cer\"";
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
                        }
                        else
                        {
                            IEnumerable<string> bundleFiles;
                            int pkgInt = 0;
                            if (o.IsModify == false)
                            {
                                bundleFiles = Directory.EnumerateFiles($".\\Temp\\{PackageName}");
                            }
                            else
                            {
                                bundleFiles = Directory.EnumerateFiles($".\\Temp\\{PackageName}\\Individual\\files");

                            }
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
                                    if (o.IsModify == false)
                                    {
                                        signtool.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \".\\Temp\\Staging\\{pkgname}\"";
                                    }
                                    else
                                    {
                                        signtool.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \".\\Temp\\{PackageName}\\Individual\\files\\{pkgname}\"";

                                    }
                                    signtool.StartInfo.RedirectStandardOutput = true;
                                    signtool.StartInfo.RedirectStandardError = true;
                                    signtool.StartInfo.UseShellExecute = false;
                                    signtool.Start();

                                    StreamReader signtoolReader = signtool.StandardOutput;
                                    StreamReader signtoolErrorReader = signtool.StandardError;

                                    string signtoolOutput = signtoolReader.ReadToEnd();
                                    if (signtoolOutput.Contains("0x8007000b"))
                                    {
                                        Console.WriteLine("Error signing package, make sure Publisher matches the AppxManifest Publisher");
                                        signtool.WaitForExit();
                                        signtool.Close();
                                        return;
                                    }
                                    Console.WriteLine(signtoolOutput + "\n" + signtoolErrorReader.ReadToEnd());
                                    signtool.WaitForExit();
                                    signtool.Close();
                                }
                            }
                        }

                        Console.WriteLine("[Creating final appxbundle]\n");
                        if (o.IsModify == false)
                        {
                            //Console.WriteLine($".\\Temp\\Staging > {OUTPUT_PATH}\\{PackageName}.appxbundle\n");
                            PackFinalBundle(".\\Temp\\Staging", $"{o.OUTPUT_PATH}\\{PackageName}.appxbundle");
                        }
                        else
                        {
                            //Console.WriteLine($".\\Temp\\{PackageName}\\Individual\\files\\ > {OUTPUT_PATH}\\{PackageName}.appxbundle");
                            if (!PackageNameWithEx.ToLower().Contains("appxbundle"))
                            {
                                PackFinalBundle(".\\Temp\\Staging", $"{o.OUTPUT_PATH}\\{PackageName}.appxbundle");
                            }
                            else
                            {
                                PackFinalBundle($".\\Temp\\{PackageName}\\Individual\\files", $"{o.OUTPUT_PATH}\\{PackageName}.appxbundle");
                            }
                        }


                        Console.WriteLine("[Signing Package (Part 2 of 2)]\n");
                        Process signtool2 = new Process();
                        signtool2.StartInfo.FileName = ".\\tools\\signtool.exe";
                        signtool2.StartInfo.Arguments = $"sign /a /v /fd SHA256 /f \".\\Temp\\{PackageName}.pfx\" \"{o.OUTPUT_PATH}\\{PackageName}.appxbundle\"";
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
                        File.Copy($@"{currentDir}\Temp\{PackageName}.pvk", $@"{o.OUTPUT_PATH}\{PackageName}_PVK.pvk", true);
                        File.Copy($@"{currentDir}\Temp\{PackageName}.pfx", $@"{o.OUTPUT_PATH}\{PackageName}_PFX.pfx", true);
                        File.Copy($@"{currentDir}\Temp\{PackageName}.cer", $@"{o.OUTPUT_PATH}\{PackageName}_CERT.cer", true);
                        ClearTempFiles();
                        Console.WriteLine("Package packed and signed successfully, Opening output folder");
                        Process.Start($"\"{o.OUTPUT_PATH}\"");
                    });


                
            }
            catch (Exception ex)
            {
                Console.WriteLine($"{ex.Message}\n\n{ex.StackTrace}\n\n{ex.Source}");
            }
        }

       /* static public void ShowUsage()
        {

            Console.WriteLine(
                "To just sign a package:\n" +
                "AppxRePack.exe -a \"Path to Appx(bundle) file\" -p \"Publisher\" -o \"Output folder\" -s \n\n" +
                "To modify a package before signing:\n" +
                "AppxRePack.exe -a \"Path to Appx(bundle) file\" -p \"Publisher\" -o \"Output folder\" -m \n\n" +
                "Note on publisher names:\n" +
                "If you recieve the error \"Error signing package, make sure Publisher matches the AppxManifest Publisher\" make sure " +
                "that the chosen publisher matches the manifest publisher, if you have a manifest with example:\n\n" +
                "\"CN=Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\"\n" +
                "you must set the publisher to:\n" +
                "\"Microsoft Windows, O=Microsoft Corporation, L=Redmond, S=Washington, C=US\"\n\n" +
                "Finally don't include the \"CN=\" Prefix when signing a package, this is added automatically");

        }*/


        public static void PackLooseFiles(string pkgPath, string PkgOutput)
        {
            var makeappx = new Process();
            makeappx.StartInfo.FileName = ".\\tools\\makeappx.exe";
            makeappx.StartInfo.Arguments = $"pack /nv /v /h SHA256 /d \"{pkgPath}\" /p \"{PkgOutput}\"";
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

        public static void PackFinalBundle(string pkgPath, string pkgOutput)
        {
            Process makeappxbundle = new Process();
            makeappxbundle.StartInfo.FileName = ".\\tools\\makeappx.exe";
            makeappxbundle.StartInfo.Arguments = $"bundle /d \"{pkgPath}\" /p \"{pkgOutput}\"";
            makeappxbundle.StartInfo.RedirectStandardOutput = true;
            makeappxbundle.StartInfo.RedirectStandardError = true;
            makeappxbundle.StartInfo.UseShellExecute = false;
            makeappxbundle.Start();

            StreamReader makeappxbundleReader = makeappxbundle.StandardOutput;
            string makeappxbundleOutput = makeappxbundleReader.ReadToEnd();
            Console.WriteLine(makeappxbundleOutput + "\n");
            makeappxbundle.WaitForExit();
            makeappxbundle.Close();

        }

        public static void ClearTempFiles()
        {
            if (!Directory.Exists(".\\Temp"))
            {
                Directory.CreateDirectory(".\\Temp");
            }
            else
            {
                Directory.Delete(".\\Temp", true);
                Directory.CreateDirectory(".\\Temp");
            }
        }
    }
}
