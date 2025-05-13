# DC.QQ.TG Update Guide

This document provides instructions for updating the DC.QQ.TG cross-platform messaging application using the provided update scripts.

## One-Click Update

We provide update scripts for both Windows and Linux/macOS systems to simplify the update process.

### Prerequisites

- **Windows**: PowerShell 5.1 or later
- **Linux/macOS**: Bash shell
- Internet connection to download dependencies
- Administrator/sudo privileges (recommended but not required)
- Git (must be installed)
- The application must have been previously installed using the installation scripts

### Windows Update

1. Download the `update.bat` or `update.ps1` script to your DC.QQ.TG installation directory
2. Right-click on `update.bat` and select "Run as administrator" or run `update.ps1` with PowerShell
3. Follow the on-screen instructions
4. The script will:
   - Backup your configuration
   - Pull the latest changes from the repository
   - Check for and install .NET 9 SDK if needed
   - Build the application
   - Restore your configuration
   - Update the desktop shortcut

```powershell
# Alternative: Run from PowerShell
.\update.ps1
```

### Linux/macOS Update

1. Download the `update.sh` script to your DC.QQ.TG installation directory
2. Make the script executable: `chmod +x update.sh`
3. Run the script: `./update.sh`
4. Follow the on-screen instructions
5. The script will:
   - Backup your configuration
   - Pull the latest changes from the repository
   - Check for and install .NET 9 SDK if needed
   - Build the application
   - Restore your configuration
   - Update the desktop shortcut (Linux only)
   - Update the symbolic link (if it exists)

```bash
# Download and run in one command
curl -sSL https://raw.githubusercontent.com/siiway/DC.QQ.TG/main/update.sh | bash
```

## Manual Update

If you prefer to update the application manually, follow these steps:

### Windows

1. Navigate to your DC.QQ.TG installation directory
2. Backup your configuration file:
   ```
   copy publish\appsettings.json publish\appsettings.backup.json
   ```
3. Pull the latest changes from the repository:
   ```
   git pull
   ```
4. Build the application:
   ```
   dotnet restore
   dotnet publish -c Release -r win-x64 -o ./publish --self-contained
   # Change win-x64 to match your system architecture
   ```
5. Restore your configuration file:
   ```
   copy publish\appsettings.backup.json publish\appsettings.json
   ```

### Linux/macOS

1. Navigate to your DC.QQ.TG installation directory
2. Backup your configuration file:
   ```
   cp publish/appsettings.json publish/appsettings.backup.json
   ```
3. Pull the latest changes from the repository:
   ```
   git pull
   ```
4. Build the application:
   ```
   dotnet restore
   # For Linux
   dotnet publish -c Release -r linux-x64 -o ./publish --self-contained
   # For macOS
   dotnet publish -c Release -r osx-x64 -o ./publish --self-contained
   # Change linux-x64 or osx-x64 to match your system architecture
   ```
5. Make the executable file executable:
   ```
   chmod +x ./publish/DC.QQ.TG
   ```
6. Restore your configuration file:
   ```
   cp publish/appsettings.backup.json publish/appsettings.json
   ```

## Troubleshooting

If you encounter issues during the update process:

1. **Git conflicts**: If you have made local changes to the code, you may encounter conflicts when pulling the latest changes. You can either:
   - Stash your changes: `git stash`
   - Reset your local repository: `git reset --hard origin/main`

2. **.NET SDK issues**: If you encounter issues with the .NET SDK, you can install it manually from [Microsoft's website](https://dotnet.microsoft.com/download/dotnet/9.0).

3. **Configuration issues**: If your configuration is lost during the update, you can restore it from the backup file or reconfigure the application.

4. **Permission issues**: If you encounter permission issues, try running the script with administrator/sudo privileges.

5. **Build errors**: If the build fails, check the error messages for details. You may need to install additional dependencies or update your .NET SDK.

For more help, please open an issue on the [GitHub repository](https://github.com/siiway/DC.QQ.TG/issues).
