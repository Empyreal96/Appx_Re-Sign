using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Package_Re_sign
{
    public class InstallerTemplate
    {
        public static string batContent = "@echo off\r\n\r\n:: BatchGotAdmin\r\n:-------------------------------------\r\nREM  --> Check for permissions\r\n>nul 2>&1 \"%SYSTEMROOT%\\system32\\cacls.exe\" \"%SYSTEMROOT%\\system32\\config\\system\"\r\n\r\nREM --> If error flag set, we do not have admin.\r\nif '%errorlevel%' NEQ '0' (\r\n    echo Requesting administrative privileges...\r\n    goto UACPrompt\r\n) else ( goto gotAdmin )\r\n\r\n:UACPrompt\r\n    echo Set UAC = CreateObject^(\"Shell.Application\"^) > \"%temp%\\getadmin.vbs\"\r\n    echo UAC.ShellExecute \"%~s0\", \"\", \"\", \"runas\", 1 >> \"%temp%\\getadmin.vbs\"\r\n\r\n    \"%temp%\\getadmin.vbs\"\r\n    exit /B\r\n\r\n:gotAdmin\r\n    if exist \"%temp%\\getadmin.vbs\" ( del \"%temp%\\getadmin.vbs\" )\r\n    pushd \"%CD%\"\r\n    CD /D \"%~dp0\"\r\n:--------------------------------------\r\n\r\npowershell -Command \"& {Import-Certificate -FilePath \\\"{certName}\\\" -CertStoreLocation Cert:\\LocalMachine\\TrustedPeople}\"\r\n\r\n\"{packageName}\"\r\n\r\nset /p \"id=Press enter key to close\"\r\n\r\n";
    }
}
