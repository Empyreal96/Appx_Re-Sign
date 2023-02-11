using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using CommandLine;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using static System.Net.Mime.MediaTypeNames;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Reflection;

namespace Package_Re_sign
{
    class Program
    {
        static string makeAppxTool = @".\tools\makeappx.exe";
        static string makeCertTool = @".\tools\makecert.exe";
        static string pvk2pfxTool = @".\tools\pvk2pfx.exe";
        static string signTool = @".\tools\signtool.exe";
        static string cmdTool = @"cmd.exe";
        static string tempPath = @".\Temp";
        static string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ1234567890";
        static string logFile = @".\Logs\log.txt"; //Will be changed by `prepareLogFile()`
        static bool isDebugOutputEnabled = true;

        #region Args define
        public class CommandOptions
        {
            [Option('a', "package", Required = true, HelpText = "The input Appx/Appxbundle or Msix/MsixBundle package to be re-signed")]
            public string Package { get; set; }

            [Option('p', "publisher", Required = false, HelpText = "The name of the publisher (Must match the AppxManifest.xml publisher).")]
            public string Publisher { get; set; }

            [Option('o', "output", Required = false, HelpText = "The desired output folder for the signed package")]
            public string Output { get; set; }

            [Option('x', "pfx", Required = false, HelpText = "PFX file for package signing")]
            public string PfxFile { get; set; }

            [Option('s', "password", Required = false, HelpText = "PFX password")]
            public string PfxPassword { get; set; }

            [Option('m', "modify", Required = false, HelpText = "Allow package modification")]
            public bool IsModify { get; set; }

            [Option('k', "skip", Required = false, HelpText = "Use this switch to apply default configuration")]
            public bool SkipDefault { get; set; }
        }
        #endregion

        #region Helpers
        static void CheckDebugOutputState()
        {
            string debugOutput = getFromAppSettings("debugOutput", "0");
            if (isValidValue(debugOutput))
            {
                isDebugOutputEnabled = debugOutput.Equals("1");
            }
        }

        static bool isBundlePackage(string path)
        {
            return path.ToLower().EndsWith(".appxbundle") || path.ToLower().EndsWith(".msixbundle");
        }
        static bool isPackage(string path)
        {
            return path.ToLower().EndsWith(".appx") || path.ToLower().EndsWith(".msix");
        }
        static bool isPFX(string path)
        {
            return path.ToLower().EndsWith(".pfx");
        }
        static bool isValidValue(string value)
        {
            return value != null && value.Trim().Length != 0;
        }

        static void cleanPath(ref string path)
        {
            path = path.Replace("\"", "");
            path = path.Replace("/", "\\");
            path = path.TrimEnd('\\');
        }
        static bool compareInput(string input, string compare)
        {
            return input != null && input.ToLower().Trim().Equals(compare);
        }

        static void clearTempFiles(string tempFolder)
        {
            if (!Directory.Exists(tempFolder))
            {
                Directory.CreateDirectory(tempFolder);
            }
            else
            {
                Directory.Delete(tempFolder, true);
                Directory.CreateDirectory(tempFolder);
            }
        }
        static string getRandomChars(int count = 5)
        {
            Random random = new Random();
            string randChar = new string(Enumerable.Repeat(chars, count).Select(s => s[random.Next(s.Length)]).ToArray());

            return randChar;
        }
        static void debugOutput(string message, bool force = false)
        {
            if (isDebugOutputEnabled || force)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(message);
                Console.ForegroundColor = ConsoleColor.White;
                WriteToLog(message);
            }
        }
        static void WriteInput(string input, bool question = false)
        {
            if (input.Contains(defaultKey))
            {
                var parts = input.Split(new string[] { defaultKey }, StringSplitOptions.None);
                if (parts != null && parts.Length == 2)
                {
                    Console.ForegroundColor = question ? ConsoleColor.Green : ConsoleColor.Magenta;
                    Console.Write(parts[0]);
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.Write(defaultKey);
                    Console.ForegroundColor = question ? ConsoleColor.Green : ConsoleColor.Magenta;
                    Console.Write(parts[1]);
                }
            }
            else
            {
                Console.ForegroundColor = question ? ConsoleColor.Green : ConsoleColor.Magenta;
                Console.Write(input);
            }
            Console.ForegroundColor = ConsoleColor.White;
            WriteToLog(input);
        }
        public static void WriteError(string input, bool writeToLog = true)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(input);
            Console.ForegroundColor = ConsoleColor.White;
            if (writeToLog)
            {
                WriteToLog(input);
            }
        }
        static void WriteWarn(string input)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine(input);
            Console.ForegroundColor = ConsoleColor.White;
            WriteToLog(input);
        }
        static void WriteSuccess(string input)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine(input);
            Console.ForegroundColor = ConsoleColor.White;
            WriteToLog(input);
        }
        static void WriteInfo(string input)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine(input);
            Console.ForegroundColor = ConsoleColor.White;
            WriteToLog(input);
        }
        static bool WriteRetry(string input)
        {
            WriteError(input);
            WriteInput("Do you want to retry? (y/n [default: y]): ", true);
            var retryProcess = ReadUserInput("");
            return !compareInput(retryProcess, "n");
        }
        static bool isArgsPassed()
        {
            return argsGlobal != null && argsGlobal.Length > 0;
        }
        static string ReadUserInput(string param = "")
        {
            string input = "";
            bool requestUserInput = true;
            Console.ForegroundColor = ConsoleColor.White;
            if (argsGlobal != null && argsGlobal.Length > 0 && param.Length > 0)
            {
                Parser.Default.ParseArguments<CommandOptions>(argsGlobal)
                    .WithParsed<CommandOptions>(o =>
                    {
                        switch (param)
                        {
                            case "package":
                                if (isValidValue(o.Package))
                                {
                                    input = o.Package;
                                    requestUserInput = false;
                                }
                                break;
                            case "output":
                                if (isValidValue(o.Output))
                                {
                                    input = o.Output;
                                    requestUserInput = false;
                                }
                                break;
                            case "publisher":
                                if (isValidValue(o.Publisher))
                                {
                                    input = o.Publisher;
                                    requestUserInput = false;
                                }
                                break;
                            case "pfx":
                                if (isValidValue(o.PfxFile))
                                {
                                    input = o.PfxFile;
                                    requestUserInput = false;
                                }
                                break;
                            case "password":
                                if (isValidValue(o.PfxPassword))
                                {
                                    input = o.PfxPassword;
                                    requestUserInput = false;
                                }
                                break;
                            case "skip":
                                if (argsGlobal.Contains("-k ") || argsGlobal.Contains("--skip"))
                                {
                                    input = o.SkipDefault ? "y" : "n";
                                    requestUserInput = false;
                                }
                                break;
                            case "modify":
                                if (argsGlobal.Contains("-m ") || argsGlobal.Contains("--modify"))
                                {
                                    input = o.IsModify ? "y" : "n";
                                    requestUserInput = false;
                                }
                                break;
                        }
                    }
                 );
            }
            if (requestUserInput)
            {
                input = Console.ReadLine();
            }
            WriteToLog(input);
            return input;
        }
        static void prepareLogFile()
        {
            string logsLocation = @".\Logs";
            if (!Directory.Exists(logsLocation))
            {
                Directory.CreateDirectory(logsLocation);
            }
            var logsName = $"log_{DateTime.Now.ToString("yyyyMMdd_hhmmss")}.txt";
            logFile = $@"{logsLocation}\{logsName}";
        }
        static void WriteToLog(string input)
        {
            try
            {
                if (input != null && input.Length > 0)
                {
                    if (!input.EndsWith("\n"))
                    {
                        input += "\n";
                    }
                    File.AppendAllText(logFile, input);
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message, false);
            }
        }
        static void newLine()
        {
            Console.WriteLine();
        }

        static string getManifestContent(string package)
        {
            string manifest = "";
            bool bundle = isBundlePackage(package);
            string packageExt = ".appx";
            string ext = Path.GetExtension(package);
            if (compareInput(ext, ".msix") || compareInput(ext, ".msixbundle"))
            {
                packageExt = ".msix";
            }
            try
            {
                using (ZipArchive archive = ZipFile.OpenRead(package))
                {
                    string lookupName = bundle ? "" : "";
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (bundle)
                        {
                            //Find any sub package
                            if (entry.Name.ToLower().EndsWith(packageExt))
                            {
                                string outputTempPackage = $@"{tempPath}\temp{packageExt}";
                                entry.ExtractToFile(outputTempPackage, true);
                                return getManifestContent(outputTempPackage);
                            }
                        }
                        else
                        {
                            if (entry.Name.Equals("AppxManifest.xml"))
                            {
                                using (Stream stream = entry.Open())
                                {
                                    using (StreamReader reader = new StreamReader(stream))
                                    {
                                        manifest = reader.ReadToEnd();
                                    }
                                }
                                break;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
            return manifest;
        }
        static string getPackagePublisher(string manifest)
        {
            string publisher = "";

            try
            {
                if (manifest != null && manifest.Length > 0)
                {
                    MatchCollection mc = Regex.Matches(manifest, "Publisher=\\\"(?<name>[^\\\"]*)\\\"", RegexOptions.Multiline);
                    foreach (Match m in mc)
                    {
                        if (m.Groups != null && m.Groups.Count > 0)
                        {
                            try
                            {
                                publisher = m.Groups["name"].Value;
                            }
                            catch (Exception ex)
                            {
                                WriteError(ex.Message);
                                //Not expected to happend, but in-case something went wrong
                                publisher = m.Value.Replace("\"", "").Replace("Publisher=", "");
                            }
                        }
                        else
                        {
                            //Not expected to happend, but in-case something went wrong
                            publisher = m.Value.Replace("\"", "").Replace("Publisher=", "");
                        }
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }

            return publisher;
        }
        static string getRegexGroupValue(string text, string regex, string group)
        {
            string value = "Unknown";
            try
            {
                MatchCollection mc = Regex.Matches(text, regex, RegexOptions.Multiline);
                foreach (Match m in mc)
                {
                    if (m.Groups != null && m.Groups.Count > 0)
                    {
                        try
                        {
                            value = m.Groups[group].Value;
                        }
                        catch (Exception ex)
                        {
                            WriteError(ex.Message);
                            //Not expected to happend, but in-case something went wrong
                            value = m.Value;
                        }
                    }
                    else
                    {
                        //Not expected to happend, but in-case something went wrong
                        value = m.Value;
                    }
                    break;
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
            return value;
        }
        static void printPackageInfo(string manifest)
        {
            try
            {
                if (manifest != null && manifest.Length > 0)
                {
                    newLine();
                    string identityRegex = "Identity Name=\\\"(?<value>[^\\\"]*)\\\"";
                    string identityValue = getRegexGroupValue(manifest, identityRegex, "value");

                    string versionRegex = "\\s+Version=\\\"(?<value>[^\\\"]*)\\\"";
                    string versionValue = getRegexGroupValue(manifest, versionRegex, "value");

                    string minBuildRegex = "\\s+MinVersion=\\\"(?<value>[^\\\"]*)\\\"";
                    string minBuildValue = getRegexGroupValue(manifest, minBuildRegex, "value");

                    string testedBuildRegex = "\\s+MaxVersionTested=\\\"(?<value>[^\\\"]*)\\\"";
                    string testedBuildValue = getRegexGroupValue(manifest, testedBuildRegex, "value");

                    WriteInfo($"- Identity     : {identityValue}");
                    WriteInfo($"- Version      : {versionValue}");
                    WriteInfo($"- Min Build    : {minBuildValue}");
                    WriteInfo($"- Tested Build : {testedBuildValue}");
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }
        #endregion

        #region Main app
        static string[] argsGlobal;
        static string defaultKey = "[ENTER]";

        [STAThread]
        static void Main(string[] args)
        {
            argsGlobal = args;
            CheckDebugOutputState();
            Start();
        }

        static void Start()
        {
        progBegin:
            bool skipDefaultConfigs = false;
            bool modifyByDefault = false;
            string defaultPublisher = "";

            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                Console.ForegroundColor = ConsoleColor.Gray;
            };

            prepareLogFile();
            try
            {
                //Print about info
                var appVersion = Assembly.GetExecutingAssembly().GetName().Version;
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write($"Package Re-Sign tool ({appVersion})");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine(" by Empyreal96");
                WriteToLog($"Package Re-Sign tool ({appVersion}) by Empyreal96");
                debugOutput("- Contributors: Bashar Astifan", true);
                debugOutput("- GitHub: https://github.com/Empyreal96/Appx_Re-Sign", true);
                WriteError("- This app provided AS-IS without any warranty");
                newLine();
                //Print help info
                WriteWarn("- If you have (.pfx) put it near to the package");
                WriteWarn("- Check (.config) for more settings");
                WriteError("- When you share this app be sure password isn't stored at (.config)");
                Task.Delay(1000).Wait();

            progStart:
                string inputPackage = "";
                string outputFolder = "";
                string packagePublisher = "";
                string pfxFile = "";

                //Check default modify state
                modifyByDefault = getFromAppSettings("modifyByDefault", "0").Equals("1");
                bool isModify = modifyByDefault;


            progInput:
                newLine();
                //Package input area
                if (isArgsPassed())
                {
                    WriteInput($"Package path: ");
                }
                else
                {
                    WriteInput($"Package path ({defaultKey} for picker): ");
                }
                inputPackage = ReadUserInput("package");

                if (!isValidValue(inputPackage))
                {
                    if (!isArgsPassed())
                    {
                        OpenFileDialog openFileDialog = new OpenFileDialog();
                        openFileDialog.Multiselect = false;
                        openFileDialog.Filter = "Packages|*.appx;*.appxbundle;*.msix;*.msixbundle";
                        DialogResult result = openFileDialog.ShowDialog();
                        if (result == DialogResult.OK)
                        {
                            inputPackage = openFileDialog.FileName;
                        }
                        else
                        {
                            goto progInput;
                        }
                    }
                    else
                    {
                        WriteError("Input package is not valid!");
                        goto progEnd;
                    }
                }
                cleanPath(ref inputPackage);
                debugOutput($"Input package: {inputPackage}");
                if (!isDebugOutputEnabled)
                {
                    debugOutput($"Input package: {Path.GetFileName(inputPackage)}", true);
                }

                //Verify input
                if (!isBundlePackage(inputPackage) && !isPackage(inputPackage))
                {
                    //File is not package
                    WriteError("Please enter appx/msix or appxbundle/msixbundle only!");
                    if (isArgsPassed())
                    {
                        goto progEnd;
                    }
                    goto progInput;
                }
                if (!File.Exists(inputPackage))
                {
                    //File not exists
                    WriteError("File is not exists!");
                    if (isArgsPassed())
                    {
                        goto progEnd;
                    }
                    goto progInput;
                }

            progSkipDefault:
                newLine();
                //Package modify area
                WriteInput($"Apply default config (y/n [{defaultKey} for 'y']): ");
                var skipDefaults = ReadUserInput("skip");

                if (!isValidValue(skipDefaults))
                {
                    newLine();
                    WriteInfo("- Output      : Input location");
                    WriteInfo("- Publisher   : Same as source");
                    WriteInfo("- Certificate : Auto");
                    WriteInfo("- Edit package: " + (isModify ? "Yes" : "No"));

                    newLine();
                    WriteInput($"Is this correct? (y/n [{defaultKey} for 'y']): ", true);
                    var confirmSkipDefaultState = ReadUserInput();
                    if (compareInput(confirmSkipDefaultState, "n"))
                    {
                        goto progSkipDefault;
                    }
                }
                skipDefaultConfigs = !compareInput(skipDefaults, "n");
                debugOutput($"Apply defaults state: {(skipDefaultConfigs ? "Enabled" : "Disabled")}", true);

            progReadPackage:
                newLine();
                //Working directories
                debugOutput($"Temp path: {tempPath}");

                //Clean temp files
                debugOutput($"Cleaning temporary files", true);
                clearTempFiles(tempPath);

                newLine();
                WriteWarn("Reading package information...");
                var manifestContent = getManifestContent(inputPackage);
                printPackageInfo(manifestContent);
                defaultPublisher = getPackagePublisher(manifestContent);
                bool publisherSetByManifest = isValidValue(defaultPublisher);

            progOutput:
                newLine();
                //Package output area
                WriteInput($"Output path [{defaultKey} to use 'package path']: ");
                if (!skipDefaultConfigs)
                {
                    outputFolder = ReadUserInput("output");
                }
                if (!isValidValue(outputFolder))
                {
                    //Let's assume that user missed the value or pressed 'enter' twice by mistake
                    //Ask before using package path as output
                    if (skipDefaultConfigs)
                    {
                        newLine();
                    }
                    WriteInput($"Use package path for output? (y/n [{defaultKey} for 'y']): ", true);
                    var usePackagePath = skipDefaultConfigs ? "y" : ReadUserInput();
                    if (!compareInput(usePackagePath, "n"))
                    {
                        debugOutput("Using package path as output", true);
                        var inputParent = Path.GetDirectoryName(inputPackage);
                        var outputNewDir = $"{inputParent}\\__PACKAGE_RESIGNED__";
                        outputFolder = outputNewDir;
                    }
                    else
                    {
                        if (isArgsPassed())
                        {
                            WriteError("Output is not valid!");
                            goto progEnd;
                        }
                        goto progOutput;
                    }
                }
                cleanPath(ref outputFolder);
                if (!Directory.Exists(outputFolder))
                {
                    debugOutput($"Output folder not exists, creating new one", true);
                    Directory.CreateDirectory(outputFolder);
                }
                debugOutput($"Output folder: {outputFolder}");


            progPublisher:
                newLine();
                //Package publisher area
                bool prePublisherSelected = false;
                if (defaultPublisher == null || defaultPublisher.Length == 0)
                {
                    defaultPublisher = getFromAppSettings("defaultPublisher");
                }
                if (isValidValue(defaultPublisher))
                {
                    WriteInput($"Publisher [{defaultKey} for '{defaultPublisher}']: ");
                }
                else
                {
                    WriteInput("Publisher: ");
                }
                if (defaultPublisher.Length == 0 || !skipDefaultConfigs)
                {
                    packagePublisher = ReadUserInput("publisher");
                }
                if (!isValidValue(packagePublisher))
                {
                    if (isValidValue(defaultPublisher))
                    {
                        if (skipDefaultConfigs)
                        {
                            newLine();
                        }
                        WriteInput($"Do you want to use ({defaultPublisher})? (y/n [{defaultKey} for 'y']): ", true);
                        var useDefaultPublisher = skipDefaultConfigs ? "y" : ReadUserInput();
                        if (!compareInput(useDefaultPublisher, "n"))
                        {
                            packagePublisher = defaultPublisher;
                            prePublisherSelected = true;
                        }
                        else
                        {
                            goto progPublisher;
                        }
                    }
                    else
                    {
                        goto progPublisher;
                    }
                }
                cleanPath(ref packagePublisher);

                if (!prePublisherSelected && !publisherSetByManifest)
                {
                    if (!isArgsPassed())
                    {
                        WriteInput($"Do you want to save it as default value? (y/n [{defaultKey} for 'y']): ", true);
                        var saveDefaultPublisher = ReadUserInput();
                        if (!compareInput(saveDefaultPublisher, "n"))
                        {
                            AddOrUpdateAppSettings("defaultPublisher", packagePublisher);
                        }
                    }
                }
                if (skipDefaultConfigs)
                {
                    newLine();
                }
                debugOutput($"Publisher: {packagePublisher}", true);


            progPFX:
                newLine();
                //Certificate area
                //Check if pfx file is near to the package
                bool prePFXSelected = false;
                var inputDirectory = Path.GetDirectoryName(inputPackage);
                var files = Directory.EnumerateFiles(inputDirectory);
                foreach (var file in files)
                {
                    if (isPFX(file))
                    {
                        WriteInfo($"PFX file detected ({Path.GetFileName(file)})");
                        WriteInput($"Do you want to use it? (y/n [{defaultKey} for 'y']): ", true);
                        var useDetectedPFX = skipDefaultConfigs ? "y" : ReadUserInput();
                        if (skipDefaultConfigs)
                        {
                            debugOutput("Applied (y)", true);
                        }
                        if (!compareInput(useDetectedPFX, "n"))
                        {
                            pfxFile = file;
                            prePFXSelected = true;
                        }
                        break;
                    }
                }
                if (!isValidValue(pfxFile))
                {
                    WriteInput($"Certificate (pfx) [{defaultKey} for 'auto']: ");
                    if (!skipDefaultConfigs)
                    {
                        pfxFile = ReadUserInput("pfx");
                    }
                    if (!isValidValue(pfxFile))
                    {
                        if (skipDefaultConfigs)
                        {
                            newLine();
                        }
                        //Let's assume that user missed the value or pressed 'enter' twice by mistake
                        //Ask before generating new certificate
                        WriteInput($"Auto generate certifcate? (y/n [{defaultKey} for 'y']): ", true);
                        var generateAutoCertificate = skipDefaultConfigs ? "y" : ReadUserInput();
                        if (skipDefaultConfigs)
                        {
                            debugOutput("Applied (y)", true);
                        }
                        if (!compareInput(generateAutoCertificate, "n"))
                        {
                            pfxFile = "";
                        }
                        else
                        {
                            goto progPFX;
                        }
                    }
                    else
                    {
                        cleanPath(ref pfxFile);
                        if (!isPFX(pfxFile))
                        {
                            WriteError("Please enter pfx file only!");
                            if (isArgsPassed())
                            {
                                goto progEnd;
                            }
                            goto progPFX;
                        }
                        if (!File.Exists(pfxFile))
                        {
                            //File not exists
                            WriteError("File is not exists!");
                            if (isArgsPassed())
                            {
                                goto progEnd;
                            }
                            goto progPFX;
                        }
                        prePFXSelected = true;
                    }
                }

            progPFXPassword:
                newLine();
                //PFX password area
                string pfxPassword = "";
                bool prePasswordSelected = false;
                string defaultPassword = getFromAppSettings("defaultPFXPassword");
                if (isValidValue(defaultPassword))
                {
                    WriteInput($"PFX password [{defaultKey} to use 'saved password']: ");
                }
                else
                {
                    WriteInput("PFX password: ");
                }
                if (defaultPassword.Length == 0 || !skipDefaultConfigs)
                {
                    pfxPassword = ReadUserInput("password");
                }

                if (!isValidValue(pfxPassword))
                {
                    if (isValidValue(defaultPassword))
                    {
                        if (skipDefaultConfigs)
                        {
                            newLine();
                        }
                        WriteInput($"Do you want to use (default password)? (y/n [{defaultKey} for 'y']): ", true);
                        var useDefaultPassword = skipDefaultConfigs ? "y" : ReadUserInput();
                        if (!compareInput(useDefaultPassword, "n"))
                        {
                            pfxPassword = defaultPassword;
                            prePasswordSelected = true;
                        }
                        else
                        {
                            WriteInput($"Skip password? (y/n [{defaultKey} for 'y']): ", true);
                            var skipPasswordState = ReadUserInput();
                            if (compareInput(skipPasswordState, "n"))
                            {
                                goto progPFXPassword;
                            }
                            debugOutput("Skipped", true);
                            pfxPassword = "";
                        }
                    }
                    else
                    {
                        WriteInput($"Skip password? (y/n [{defaultKey} for 'y']): ", true);
                        var skipPasswordState = ReadUserInput();
                        if (compareInput(skipPasswordState, "n"))
                        {
                            goto progPFXPassword;
                        }
                        debugOutput("Skipped", true);
                        pfxPassword = "";
                    }
                }

                if (isValidValue(pfxPassword) && !prePasswordSelected)
                {
                    if (!isArgsPassed())
                    {
                        WriteInput($"Do you want to save it as default value? (y/n [{defaultKey} for 'y']): ", true);
                        var saveDefaultPassword = ReadUserInput();
                        if (!compareInput(saveDefaultPassword, "n"))
                        {
                            AddOrUpdateAppSettings("defaultPFXPassword", pfxPassword);
                        }
                    }
                }

            progModify:
                newLine();
                //Package modify area
                WriteInput($"Modify (y/n [{defaultKey} for {(modifyByDefault ? 'y' : 'n')}]): ");

                if (isArgsPassed())
                {
                    if (skipDefaultConfigs)
                    {
                        isModify = argsGlobal.Contains("--modify");
                    }
                }

                if (!skipDefaultConfigs)
                {
                    var modifyInput = ReadUserInput("modify");
                    if (!isValidValue(modifyInput))
                    {
                        WriteInput($"Modify is {(modifyByDefault ? "on" : "off")}, correct? (y/n [{defaultKey} for 'y']): ", true);
                        var confirmModifyState = ReadUserInput();
                        if (compareInput(confirmModifyState, "n"))
                        {
                            goto progModify;
                        }
                    }
                    else
                    {
                        isModify = compareInput(modifyInput, "y");
                    }
                }
                debugOutput($"Modify state: {(isModify ? "Enabled" : "Disabled")}", true);


            progResign:
                newLine();

                //Package basic info
                bool isBundle = isBundlePackage(inputPackage);
                debugOutput($"Is package bundle: {(isBundle ? "True" : "False")}", true);

                string packageName = Path.GetFileNameWithoutExtension(inputPackage);
                string packageNameWithEx = Path.GetFileName(inputPackage);
                string packageExt = Path.GetExtension(inputPackage);
                debugOutput($"Output package name: {packageName}", true);
                //Resolve output folder to avoid placing files in root
                outputFolder = Path.Combine(outputFolder, packageName);
                if (!Directory.Exists(outputFolder))
                {

                    debugOutput($"Output folder ({Path.GetFileName(outputFolder)}) not exists, creating new one..");
                    if (!isDebugOutputEnabled)
                    {
                        debugOutput($"Output folder not exists, creating new one..", true);
                    }
                    Directory.CreateDirectory(outputFolder);
                }
                else
                {
                    //To avoid replacement just append random chars
                    debugOutput($"Output folder already exists, resolving..");
                    //Append random chars to the output name
                    string randChar = getRandomChars();
                    outputFolder = $"{outputFolder}_{randChar}";
                    debugOutput($"Output folder (new) name: {outputFolder}");
                    Directory.CreateDirectory(outputFolder);
                }

                //Check if output exists
                if (File.Exists($"{outputFolder}\\{packageName}{packageExt}"))
                {
                    debugOutput($"Output file already exists, resolving..");
                    //Append random chars to the output name
                    string randChar = getRandomChars();
                    packageName = $"{packageName}_{randChar}";
                    debugOutput($"Output package (new) name: {packageName}");
                }

                //Extract package
                var extractState = ExtractPackage(inputPackage, tempPath, packageName, isBundle);
                if (extractState)
                {
                    if (isModify)
                    {
                        //Open temp folder
                        Process.Start($"\"{tempPath}\\{packageName}\"");

                        //Wait if modify required
                        WriteWarn("After making changes, press any key to continue");
                        ReadUserInput();
                    }

                progPackaging:
                    if (!isValidValue(pfxFile))
                    {
                        //User choose auto certificate
                        var newCert = MakeCert(packagePublisher, outputFolder, packageName, pfxPassword);
                        if (newCert.isValid)
                        {
                            pfxFile = newCert.pfxFile.pfxPath;
                        }
                        else
                        {
                            //When errors detected give ability to user to retry
                            if (WriteRetry("Errors detected while creating certificate.."))
                            {
                                goto progPackaging;
                            }
                        }
                    }

                    //Check pfx again before generating package
                    if (isValidValue(pfxFile))
                    {
                    progGeneratePackage:
                        //Generate package area
                        var generateState = GeneratePackage(tempPath, packageName, packageExt, pfxFile, isBundle, pfxPassword);
                        if (!generateState.isValid)
                        {
                            //When errors detected give ability to user to retry
                            if (WriteRetry("Errors detected while generating final package.."))
                            {
                                goto progGeneratePackage;
                            }
                        }
                        else
                        {
                        progCopyPackage:
                            //Copy final package area
                            WriteInfo("Copying package from temp to output..");
                            var copyState = generateState.Copy(outputFolder);
                            if (!copyState)
                            {
                                //When errors detected give ability to user to retry
                                if (WriteRetry("Errors detected while copying final package.."))
                                {
                                    goto progCopyPackage;
                                }
                            }
                            else
                            {
                                if (prePFXSelected)
                                {
                                progCreateCert:
                                    //Generate .cer from pfx area
                                    var createFromPFXState = MakeCertFromPFX(pfxFile, outputFolder, packageName, pfxPassword);
                                    if (!createFromPFXState)
                                    {
                                        //When errors detected give ability to user to retry
                                        if (WriteRetry("Unable to generate (.cer) from (.pfx).."))
                                        {
                                            WriteInput("PFX password: ");
                                            pfxPassword = ReadUserInput();
                                            goto progCreateCert;
                                        }
                                    }
                                }

                                var depsFolder = $@"{inputDirectory}\Dependencies";
                                if (Directory.Exists(depsFolder))
                                {
                                    //Copy dependencies
                                    WriteInfo("Appending Dependencies to output..");
                                    var depsOutputFolder = $@"{outputFolder}\Dependencies";
                                    if (!Directory.Exists(depsOutputFolder))
                                    {
                                        Directory.CreateDirectory(depsOutputFolder);
                                    }
                                    CopyFilesRecursively(depsFolder, depsOutputFolder);
                                }

                                //Copy any default folders/files specified by config
                                WriteInfo("Copy folders by config to output (if any)..");
                                CopyFoldersByCofig(inputDirectory, outputFolder);
                                WriteInfo("Copy files by config to output (if any)..");
                                CopyFilesByCofig(inputDirectory, outputFolder);

                                //Generate installer
                                WriteInfo("Generating installer (.bat)");
                                var expectedCertificateName = $"{packageName}.cer";
                                var expectedpackageName = $"{packageName}{packageExt}";
                                var installerPath = $@"{outputFolder}\{packageName} (Installer).bat";
                                var installerContent = InstallerTemplate.batContent
                                    .Replace("{certName}", expectedCertificateName)
                                    .Replace("{packageName}", expectedpackageName);

                                File.WriteAllText(installerPath, installerContent);

                                WriteSuccess("Package successfully re-signed.");

                                //Open package folder
                                Process.Start($"{outputFolder}");
                            }
                        }
                    }

                }
                else
                {
                    //When errors detected give ability to user to retry
                    if (WriteRetry("Errors detected while extract.."))
                    {
                        goto progResign;
                    }
                }
                if (!isArgsPassed())
                {
                    newLine();
                    WriteInput($"Do you want to re-sign another package? (y/n [{defaultKey} for 'y']): ", true);
                    var morePackages = ReadUserInput();
                    if (!compareInput(morePackages, "n"))
                    {
                        Console.Clear();
                        goto progBegin;
                    }
                }

            }
            catch (Exception ex)
            {
                WriteError($"{ex.Message}\n\n{ex.StackTrace}\n\n{ex.Source}");
                ReadUserInput();
                Console.Clear();
                goto progBegin;
            }
        progEnd:
            Console.ForegroundColor = ConsoleColor.Gray;
            WriteToLog("App closed");
        }
        #endregion

        #region Package helpers
        static bool ExtractPackage(string input, string output, string name, bool bundle = false)
        {
            WriteInfo("Extracting package..");

            Error[] errorsList = new Error[] {
              new Error("tempErrorCode", "tempErrorMessage"),
            };

            bool foundErrors = false;

            var outputPath = $@"{output}\{name}";
            var packagesPath = outputPath;
            if (!Directory.Exists(outputPath))
            {
                debugOutput($"Output not exists, creating: {outputPath}");
                Directory.CreateDirectory(outputPath);
            }

            if (bundle)
            {
                //Extra work needed
                packagesPath = $@"{outputPath}\packages";
                if (!Directory.Exists(packagesPath))
                {
                    debugOutput($"Packages output not exists, creating: {packagesPath}");
                    Directory.CreateDirectory(packagesPath);
                }
            }

            Process makeappx = new Process();
            makeappx.StartInfo.FileName = makeAppxTool;
            string command = $"{(bundle ? "unbundle" : "unpack")} /v /p \"{input}\" /d \"{(bundle ? packagesPath : outputPath)}\"";
            makeappx.StartInfo.Arguments = command;
            debugOutput($"{makeAppxTool} {command}");
            makeappx.StartInfo.RedirectStandardOutput = true;
            makeappx.StartInfo.RedirectStandardError = true;
            makeappx.StartInfo.UseShellExecute = false;
            makeappx.Start();

            StreamReader makeappxReader = makeappx.StandardOutput;
            string makeappxOutput = makeappxReader.ReadToEnd();
            debugOutput(makeappxOutput);

            foreach (var error in errorsList)
            {
                if (makeappxOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            makeappx.WaitForExit();
            makeappx.Close();

            if (bundle)
            {
                debugOutput("Bundle detected, extracting packages..");
                //Extract packages to output path
                var bundleFiles = Directory.EnumerateFiles(packagesPath);
                foreach (var file in bundleFiles)
                {
                    if (isPackage(file))
                    {
                    progPackageExtract:
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var extractState = ExtractPackage(file, outputPath, fileName, false);
                        if (!extractState)
                        {
                            //When errors detected give ability to user to retry
                            if (WriteRetry($"Errors detected while extracting {file}.."))
                            {
                                goto progPackageExtract;
                            }
                            else
                            {
                                foundErrors = true;
                                break;
                            }
                        }
                    }
                }
            }

            return !foundErrors;
        }
        public static OutputPackage GeneratePackage(string input, string name, string ext, string pfx, bool bundle = false, string password = "")
        {
            OutputPackage outputPackage = new OutputPackage("", "", false);
            bool foundErrors = true;

            var defaultPackageExt = ".appx";
            var defaultBundleExt = ".appxbundle";
            if (compareInput(ext, ".msix") || compareInput(ext, ".msixbundle"))
            {
                defaultPackageExt = ".msix";
                defaultBundleExt = ".msixbundle";
            }
            debugOutput($"Default extensions: {defaultPackageExt}, {defaultBundleExt}");

            var inputPath = $@"{input}\{name}";
            var outputPath = $@"{input}\output";
            var packagesPath = inputPath;
            var packagesOutputPath = outputPath;
            debugOutput("Working locations (Single):");
            debugOutput($"Package folder: {inputPath}");
            debugOutput($"Output: {outputPath}");

            if (Directory.Exists(inputPath))
            {
                if (!Directory.Exists(outputPath))
                {
                    debugOutput($"Output not exists, creating: {outputPath}");
                    Directory.CreateDirectory(outputPath);
                }

                if (bundle)
                {
                    //Create bundle
                    packagesPath = $@"{input}\{name}";
                    packagesOutputPath = $@"{input}\{name}\packages\output";
                    debugOutput("Working locations (Bundle):");
                    debugOutput($"Packages folder: {packagesPath}");
                    debugOutput($"Output packages: {packagesOutputPath}");

                    if (Directory.Exists(packagesPath))
                    {
                        if (!Directory.Exists(packagesOutputPath))
                        {
                            debugOutput($"Output packages not exists, creating: {packagesOutputPath}");
                            Directory.CreateDirectory(packagesOutputPath);
                        }

                        //Generate packages
                        var makePackagesState = MakePackages(packagesPath, packagesOutputPath, defaultPackageExt);
                        if (makePackagesState)
                        {
                            //Sign packages before bundle
                            var signStats = SignPackages(pfx, packagesOutputPath);
                            if (signStats)
                            {
                            progMakeBundle:
                                //Make bundle area
                                var bundleName = $"{name}{defaultBundleExt}";
                                var bundlePath = $@"{outputPath}\{bundleName}";
                                var makeBundleState = MakePackageBundle(packagesOutputPath, bundlePath);
                                if (makeBundleState)
                                {
                                progBundleSign:
                                    //Sign bundle area
                                    signStats = SignPackage(pfx, bundlePath, password);
                                    if (!signStats)
                                    {
                                        //When errors detected give ability to user to retry
                                        if (WriteRetry($"Errors detected while signing bundle: {bundlePath}"))
                                        {
                                            goto progBundleSign;
                                        }
                                    }
                                    else
                                    {
                                        //When we reach this point then no errors detected
                                        foundErrors = false;
                                        outputPackage.bundleFile = bundlePath;
                                        WriteSuccess("Bundle succesfully generated.");
                                    }
                                }
                                else
                                {
                                    //When errors detected give ability to user to retry
                                    if (WriteRetry($"Errors detected while creating bundle for {packagesOutputPath}"))
                                    {
                                        goto progMakeBundle;
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                //Create single package
                progCreatePackage:
                    var packageName = $"{name}{defaultPackageExt}";
                    var packagePath = $@"{outputPath}\{packageName}";
                    var makePackageState = MakePackage(inputPath, packagePath);
                    if (makePackageState)
                    {
                    progSignPackage:
                        //Sign package area
                        var signStats = SignPackage(pfx, packagePath, password);
                        if (!signStats)
                        {
                            //When errors detected give ability to user to retry
                            if (WriteRetry($"Errors detected while signing package: {packagePath}"))
                            {
                                goto progSignPackage;
                            }
                        }
                        else
                        {
                            //When we reach this point then no errors detected
                            foundErrors = false;
                            outputPackage.packageFile = packagePath;
                            WriteSuccess("Package succesfully generated.");
                        }
                    }
                    else
                    {
                        //When errors detected give ability to user to retry
                        if (WriteRetry($"Errors detected while creating package for {inputPath}"))
                        {
                            goto progCreatePackage;
                        }
                    }
                }

            }

            outputPackage.isValid = !foundErrors;
            return outputPackage;
        }
        public static bool MakePackages(string pkgsPath, string pkgsOutput, string pkgsExt)
        {
            bool foundErrors = false;

            var bundleFolders = Directory.EnumerateDirectories(pkgsPath);
            foreach (var folder in bundleFolders)
            {
                if (File.Exists($@"{folder}\AppxManifest.xml"))
                {
                progMakePackage:
                    //Make package area
                    var packagePath = $@"{pkgsOutput}\{Path.GetFileName(folder)}{pkgsExt}";
                    var packState = MakePackage(folder, packagePath);
                    if (!packState)
                    {
                        //When errors detected give ability to user to retry
                        if (WriteRetry($"Errors detected while packing {folder}.."))
                        {
                            goto progMakePackage;
                        }
                        else
                        {
                            foundErrors = true;
                            break;
                        }
                    }
                }
            }

            return !foundErrors;
        }
        public static bool MakePackage(string pkgPath, string PkgOutput)
        {
            WriteInfo($"Making package for {Path.GetFileName(pkgPath)}");

            Error[] errorsList = new Error[] {
              new Error("tempErrorCode", "tempErrorMessage"),
            };

            bool foundErrors = false;

            var makeappx = new Process();
            makeappx.StartInfo.FileName = makeAppxTool;
            string command = $"pack /nv /v /h SHA256 /d \"{pkgPath}\" /p \"{PkgOutput}\"";
            makeappx.StartInfo.Arguments = command;
            debugOutput($"{makeAppxTool} {command}");
            makeappx.StartInfo.RedirectStandardOutput = true;
            makeappx.StartInfo.RedirectStandardError = true;
            makeappx.StartInfo.UseShellExecute = false;
            makeappx.Start();

            StreamReader makeappxReader = makeappx.StandardOutput;
            string makeappxOutput = makeappxReader.ReadToEnd();
            debugOutput(makeappxOutput);

            foreach (var error in errorsList)
            {
                if (makeappxOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            makeappx.WaitForExit();
            makeappx.Close();

            return !foundErrors;
        }

        public static bool MakePackageBundle(string pkgPath, string pkgOutput)
        {
            WriteInfo($"Making package bundle for {Path.GetFileName(pkgPath)}..");

            Error[] errorsList = new Error[] {
              new Error("tempErrorCode", "tempErrorMessage"),
            };

            bool foundErrors = false;

            Process makeappx = new Process();
            makeappx.StartInfo.FileName = makeAppxTool;
            string command = $"bundle /d \"{pkgPath}\" /p \"{pkgOutput}\"";
            makeappx.StartInfo.Arguments = command;
            debugOutput($"{makeAppxTool} {command}");
            makeappx.StartInfo.RedirectStandardOutput = true;
            makeappx.StartInfo.RedirectStandardError = true;
            makeappx.StartInfo.UseShellExecute = false;
            makeappx.Start();

            StreamReader makeappxReader = makeappx.StandardOutput;
            string makeappxOutput = makeappxReader.ReadToEnd();
            debugOutput(makeappxOutput);

            foreach (var error in errorsList)
            {
                if (makeappxOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            makeappx.WaitForExit();
            makeappx.Close();

            return !foundErrors;
        }
        #endregion

        #region Certificate helpers
        static string makeCertFromPFXDebugOutput = "";
        static bool MakeCertFromPFX(string pfx, string output, string name, string password = "")
        {
            var pfxName = $"{Path.GetFileNameWithoutExtension(pfx)}";
            var cerFile = $@"{output}\{name}.cer";

            WriteInfo("Generating certificate from pfx file..");

            Error[] errorsList = new Error[] {
              new Error("password is not correct.", "the specified network password is not correct."),
            };

            makeCertFromPFXDebugOutput = "";

            bool foundErrors = false;

            var makecert = new Process();
            makecert.StartInfo.FileName = cmdTool;
            makecert.StartInfo.RedirectStandardInput = true;
            makecert.StartInfo.RedirectStandardOutput = true;
            makecert.StartInfo.RedirectStandardError = true;
            makecert.StartInfo.CreateNoWindow = true;
            makecert.StartInfo.UseShellExecute = false;

            makecert.OutputDataReceived += Makecert_OutputDataReceived;
            makecert.ErrorDataReceived += Makecert_ErrorDataReceived;

            makecert.Start();
            makecert.BeginOutputReadLine();
            makecert.BeginErrorReadLine();

            string exportCommand = $"powershell -Command \"& {{$Cert = (New-Object System.Security.Cryptography.X509Certificates.X509Certificate2); $Cert.Import(\\\"{pfx}\\\", \\\"{password}\\\", 'DefaultKeySet'); Export-Certificate -Cert $Cert -FilePath \\\"{cerFile}\\\" -Type CERT\"}}";
            debugOutput($"PowerShell command: {exportCommand}");
            makecert.StandardInput.WriteLine(exportCommand);
            makecert.StandardInput.Flush();

            makecert.WaitForExit(3000);
            makecert.Close();

            makecert.OutputDataReceived -= Makecert_OutputDataReceived;
            makecert.ErrorDataReceived -= Makecert_ErrorDataReceived;
            foreach (var error in errorsList)
            {
                if (makeCertFromPFXDebugOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            return !foundErrors;
        }

        private static void Makecert_ErrorDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                makeCertFromPFXDebugOutput += e.Data;
                WriteError(e.Data);
            }
        }

        private static void Makecert_OutputDataReceived(object sender, DataReceivedEventArgs e)
        {
            if (e.Data != null)
            {
                makeCertFromPFXDebugOutput += e.Data;
                debugOutput(e.Data);
            }
        }

        static Cert MakeCert(string publisher, string output, string name, string password = "")
        {
            WriteInfo("Generating new certificate..");

            Error[] errorsList = new Error[] {
              new Error("tempErrorCode", "tempErrorMessage"),
            };

            bool foundErrors = false;

            var pvkFile = MakePVK(output, name, password);

            string cer = $@"{output}\{name}.cer";
            string pvk = pvkFile.pvkPath;
            var makecert = new Process();
            makecert.StartInfo.FileName = makeCertTool;
            string command = $"-r -h 0 -n \"{publisher}\" -pe -sv \"{pvk}\" \"{cer}\"";
            makecert.StartInfo.Arguments = command;
            debugOutput($"{makeCertTool} {command}");
            makecert.StartInfo.RedirectStandardOutput = true;
            makecert.StartInfo.RedirectStandardError = true;
            makecert.StartInfo.UseShellExecute = false;
            makecert.Start();

            StreamReader makecertReader = makecert.StandardOutput;
            string makecertOutput = makecertReader.ReadToEnd();
            debugOutput(makecertOutput);

            foreach (var error in errorsList)
            {
                if (makecertOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            makecert.WaitForExit();
            makecert.Close();

            var pfxFile = MakePFX(cer, pvkFile.pvkPath, output, name, password);
            foundErrors = foundErrors || !pfxFile.isValid || !pvkFile.isValid;
            return new Cert(cer, pvkFile, pfxFile, !foundErrors);
        }

        static PVK MakePVK(string output, string name, string password = "")
        {
            WriteInfo("Generating new private key..");

            Error[] errorsList = new Error[] {
              new Error("tempErrorCode", "tempErrorMessage"),
            };

            bool foundErrors = false;

            string pvk = $@"{output}\{name}.pvk";
            try
            {
                RSACryptoServiceProvider rsaCSP = new RSACryptoServiceProvider();
                using (var outputPVK = new BinaryWriter(File.Create(pvk)))
                {
                    outputPVK.Write(0xB0B5F11Eu);  // PVK magic number
                    outputPVK.Write(0u);
                    outputPVK.Write(1u);           // KEYTYPE_KEYX for RSA
                    outputPVK.Write(0u);           // not encrypted
                    outputPVK.Write(0u);           // encryption salt length

                    var cspBlob = rsaCSP.ExportCspBlob(true);
                    outputPVK.Write((uint)cspBlob.Length);
                    outputPVK.Write(cspBlob);
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
                foundErrors = true;
            }

            return new PVK(pvk, !foundErrors);
        }

        static PFX MakePFX(string cerFile, string pvkFile, string output, string name, string password = "")
        {
            WriteInfo("Generating code signing certificate (PFX)..");

            Error[] errorsList = new Error[] {
              new Error("0x8007000b", "Bad file format"),
              new Error("0x80070490", "Cannot find certificates that match the key"),
              new Error("0x800704c7", "Operation cancelled"),
            };

            bool foundErrors = false;

            string passwordParm = password.Length > 0 ? $"-pi \"{password}\" -po \"{password}\"" : "";

            string pfx = $@"{output}\{name}.pfx";
            Process pvk2pfx = new Process();
            pvk2pfx.StartInfo.FileName = pvk2pfxTool;
            string command = $"-pvk \"{pvkFile}\" -spc \"{cerFile}\" -pfx \"{pfx}\" {passwordParm} -f";
            pvk2pfx.StartInfo.Arguments = command;
            debugOutput($"{pvk2pfxTool} {command}");
            pvk2pfx.StartInfo.RedirectStandardOutput = true;
            pvk2pfx.StartInfo.RedirectStandardError = true;
            pvk2pfx.StartInfo.UseShellExecute = false;
            pvk2pfx.Start();

            StreamReader pvk2pfxReader = pvk2pfx.StandardOutput;
            string pvk2pfxOutput = pvk2pfxReader.ReadToEnd();
            debugOutput(pvk2pfxOutput);

            foreach (var error in errorsList)
            {
                if (pvk2pfxOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            pvk2pfx.WaitForExit();
            pvk2pfx.Close();

            return new PFX(pfx, !foundErrors);
        }

        static bool SignPackage(string pfxFile, string package, string password = "")
        {
            WriteInfo($"Signing package: {Path.GetFileName(package)}");

            Error[] errorsList = new Error[] {
              new Error("0x8007000b", "Make sure Publisher matches the AppxManifest Publisher"),
              new Error("password is not correct", "The specified PFX password is not correct"),
            };

            bool foundErrors = false;
            string passwordParm = password.Length > 0 ? $"/p \"{password}\"" : "";
            Process signtool = new Process();
            signtool.StartInfo.FileName = signTool;
            string command = $"sign /a /v {passwordParm} /fd SHA256 /f \"{pfxFile}\" \"{package}\"";
            signtool.StartInfo.Arguments = command;
            debugOutput($"{signTool} {command}");
            signtool.StartInfo.RedirectStandardOutput = true;
            signtool.StartInfo.RedirectStandardError = true;
            signtool.StartInfo.UseShellExecute = false;
            signtool.Start();

            StreamReader signtoolReader = signtool.StandardOutput;

            string signtoolOutput = signtoolReader.ReadToEnd();

            foreach (var error in errorsList)
            {
                if (signtoolOutput.Contains(error.errorCode))
                {
                    WriteError(error.errorMessage);
                    foundErrors = true;
                }
            }

            signtool.WaitForExit();
            signtool.Close();

            return !foundErrors;
        }

        static bool SignPackages(string pfxFile, string packages, string password = "")
        {
            WriteInfo($"Signing packages..");

            bool foundErrors = false;

            var bundleFiles = Directory.EnumerateFiles(packages);
            foreach (var file in bundleFiles)
            {
                if (isPackage(file))
                {
                progSignPackage:
                    var signState = SignPackage(pfxFile, file, password);
                    if (!signState)
                    {
                        //When errors detected give ability to user to retry
                        if (WriteRetry($"Errors detected while signing {file}.."))
                        {
                            goto progSignPackage;
                        }
                        else
                        {
                            foundErrors = true;
                            break;
                        }
                    }
                }
            }

            return !foundErrors;
        }
        #endregion

        #region Extra helpers
        private static void CopyFilesRecursively(string sourcePath, string targetPath)
        {
            try
            {
                //Now Create all of the directories
                foreach (string dirPath in Directory.GetDirectories(sourcePath, "*", SearchOption.AllDirectories))
                {
                    try
                    {
                        Directory.CreateDirectory(dirPath.Replace(sourcePath, targetPath));
                    }
                    catch (Exception ex)
                    {
                        WriteError(ex.Message);
                    }
                }

                //Copy all the files & Replaces any files with the same name
                foreach (string newPath in Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories))
                {
                    try
                    {
                        File.Copy(newPath, newPath.Replace(sourcePath, targetPath), true);
                    }
                    catch (Exception ex)
                    {
                        WriteError(ex.Message);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }
        public static void AddOrUpdateAppSettings(string key, string value)
        {
            try
            {
                var configFile = ConfigurationManager.OpenExeConfiguration(ConfigurationUserLevel.None);
                var settings = configFile.AppSettings.Settings;
                if (settings[key] == null)
                {
                    settings.Add(key, value);
                }
                else
                {
                    settings[key].Value = value;
                }
                configFile.Save(ConfigurationSaveMode.Modified);
                ConfigurationManager.RefreshSection(configFile.AppSettings.SectionInformation.Name);
            }
            catch (Exception ex)
            {
                WriteError("Config file is missing, or something went wrong!");
                WriteError(ex.Message);
            }
        }
        public static string getFromAppSettings(string key, string def = "")
        {
            string value = def;
            try
            {
                value = ConfigurationManager.AppSettings[key];
            }
            catch (Exception ex)
            {
                WriteError("Config file is missing, or something went wrong!");
                WriteError(ex.Message);
            }
            return value;
        }
        static void CopyFoldersByCofig(string input, string output)
        {
            try
            {
                var folders = getFromAppSettings("foldersToCopy");
                if (isValidValue(folders))
                {
                    var folderArray = folders.Split('|');
                    if (folderArray != null && folderArray.Length > 0)
                    {
                        foreach (var folder in folderArray)
                        {
                            try
                            {
                                var folderPath = $@"{input}\{folder}";
                                if (Directory.Exists(folderPath))
                                {
                                    var outputPath = $@"{output}\{folder}";
                                    if (!Directory.Exists(outputPath))
                                    {
                                        Directory.CreateDirectory(outputPath);
                                    }
                                    CopyFilesRecursively(folderPath, outputPath);
                                }
                            }
                            catch (Exception exp)
                            {
                                WriteError(exp.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }

        static void CopyFilesByCofig(string input, string output)
        {
            try
            {
                var files = getFromAppSettings("filesToCopy");
                if (isValidValue(files))
                {
                    var filesArray = files.Split('|');
                    if (filesArray != null && filesArray.Length > 0)
                    {
                        foreach (var file in filesArray)
                        {
                            try
                            {
                                var filePath = $@"{input}\{file}";
                                if (File.Exists(filePath))
                                {
                                    var outputPath = $@"{output}\{file}";
                                    File.Copy(filePath, outputPath, true);
                                }
                            }
                            catch (Exception exp)
                            {
                                WriteError(exp.Message);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                WriteError(ex.Message);
            }
        }

        #endregion
    }
    class Cert
    {
        public string cerFile;
        public PVK pvkFile;
        public PFX pfxFile;
        public bool isValid = false;
        public Cert(string cerFile, PVK pvkFile, PFX pfxFile, bool isValid)
        {
            this.cerFile = cerFile;
            this.pvkFile = pvkFile;
            this.pfxFile = pfxFile;
            this.isValid = isValid;
        }
    }
    class PFX
    {
        public string pfxPath;
        public bool isValid = false;
        public PFX(string pfxPath, bool isValid)
        {
            this.pfxPath = pfxPath;
            this.isValid = isValid;
        }
    }
    class PVK
    {
        public string pvkPath;
        public bool isValid = false;
        public PVK(string pvkPath, bool isValid)
        {
            this.pvkPath = pvkPath;
            this.isValid = isValid;
        }
    }
    class Error
    {
        public string errorCode;
        public string errorMessage;
        public Error(string errorCode, string errorMessage)
        {
            this.errorCode = errorCode;
            this.errorMessage = errorMessage;
        }
    }
    class OutputPackage
    {
        public string packageFile;
        public string bundleFile;
        public bool isValid = false;
        public OutputPackage(string packageFile, string bundleFile, bool isValid)
        {
            this.packageFile = packageFile;
            this.bundleFile = bundleFile;
            this.isValid = isValid;
        }
        public bool Copy(string output)
        {
            if (isValid)
            {
                try
                {
                    var fileName = "";
                    var filePath = "";
                    var tempFile = packageFile;
                    if (packageFile.Length > 0)
                    {
                        fileName = Path.GetFileName(packageFile);
                        filePath = $@"{output}\{fileName}";
                    }
                    else
                    {
                        tempFile = bundleFile;
                        fileName = Path.GetFileName(bundleFile);
                        filePath = $@"{output}\{fileName}";
                    }
                    File.Copy(tempFile, filePath, true);
                    return true;
                }
                catch (Exception ex)
                {
                    Program.WriteError(ex.Message);
                }
            }

            return false;
        }
    }
}
