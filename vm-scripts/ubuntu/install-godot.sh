#!/bin/bash
# installs dotnet 9.x for ubuntu

# Save original shell settings
original_settings="$-"

# Consolidated bash options: exit on error/undefined vars, show commands, catch pipeline errors
set -euxo pipefail


CURRENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"   # absolute dir of script

INSTALL_DIR="$(realpath "$CURRENT_DIR/../../bin/installers")"  # resolve .. and symlinks

sudo apt install 7zip

pushd $INSTALL_DIR

GODOT_NAME="Godot_v4.5-dev5_mono_linux_x86_64"
wget https://github.com/godotengine/godot-builds/releases/download/4.5-dev5/$GODOT_NAME.zip 
7z x $GODOT_NAME.zip -o../Godot
popd

GODOT_DIR="$(realpath "$CURRENT_DIR/../../bin/Godot/$GODOT_NAME")"  # resolve .. and symlinks
pushd $GODOT_DIR





# echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
# echo %%%%%%%%%%%%%%%  DOTNET 9 START: ${BASH_SOURCE[0]}

# DOTNET_INSTALLER=dotnet-sdk-10.0.100-preview.4.25258.110-linux-x64.tar.gz
# DOTNET_INSTALLER_URL=https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.100-preview.4.25258.110/$DOTNET_INSTALLER
# DOTNET_INSTALL_DIRNAME=dotnet10


# # set envvars needed
# export DOTNET_ENVIRONMENT=Test
# export ASPNETCORE_ENVIRONMENT=Test
# # needed for microsoft logging failure in VM's
# export MSBUILDTERMINALLOGGER=false
# export COLUMNS=120
# # no telemetry
# export DOTNET_NOLOGO=true
# export DOTNET_CLI_TELEMETRY_OPTOUT=true

# DOTNET_BIN_DIR="$(realpath "$CURRENT_DIR/../../bin/$DOTNET_INSTALL_DIRNAME")"  # resolve .. and symlinks
# original_user="$(whoami)"
# # ensure we update the invoking user's bashrc, even when running with sudo
# target_user="${SUDO_USER:-${USER:-root}}"
# target_home="$(eval echo "~$target_user")"

# # # if dotnet --version is already installed, skip installation
# # if command -v dotnet &> /dev/null; then
# #     echo "dotnet is already installed, skipping installation."
# #     dotnet --version
# #     return 0
# # fi

# # # if $DOTNET_BIN_DIR/dotnet-env.sh exists, source it and exit
# # if [ -f "/etc/profile.d/dotnet.sh" ]; then
# #     echo "dotnet-env.sh already exists, sourcing it."
# #     source /etc/profile.d/dotnet.sh

# #     # also add to ~/.bashrc if not already there
# #     if ! grep -q "source /etc/profile.d/dotnet.sh" ~/.bashrc; then
# #         echo "source /etc/profile.d/dotnet.sh" >> ~/.bashrc
# #     fi
# #     echo "Environment variables sourced from /etc/profile.d/dotnet.sh"
# #     echo "Current .NET version:"
# #     dotnet --version
# #     return 0
# # fi


# mkdir -p $DOTNET_BIN_DIR
# INSTALL_DIR="$(realpath "$DOTNET_BIN_DIR/../installers")"  # resolve .. and symlinks
# mkdir -p $INSTALL_DIR

# pushd $INSTALL_DIR

# ## install dotnet from repository, doesn't work on Jules because of Python error.
# # sudo apt-get update -y                             # update package lists
# # sudo apt-get install -y software-properties-common   # provides add-apt-repository  
# # sudo add-apt-repository ppa:dotnet/backports -y     # registers Canonical’s .NET backports repo.  source: https://pupli.net/2025/03/12/install-net-sdk-9-0-on-ubuntu-22-04/
# # sudo apt-get update -y                               # include newly added dotnet/backports packages 
# # sudo apt-get install -y dotnet-sdk-9.0            # installs .NET 9 from the backports PPA
# # dotnet --version

# # install dotnet manually: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.300-linux-x64-binaries




# # check if .gz already exists, if so, skip download
# if [ -f "$DOTNET_INSTALLER" ]; then
#     echo "$DOTNET_INSTALLER already exists, skipping download."
# else
#     echo "Downloading $DOTNET_INSTALL_DIRNAME..."
#     wget $DOTNET_INSTALLER_URL    
#     sudo apt update -y
# fi

# #sudo mkdir -p /usr/share/dotnet 
# sudo tar zxf $DOTNET_INSTALLER -C $DOTNET_BIN_DIR


# # # Create symlink to dotnet executable in current directory
# # ln -sf "$DOTNET_BIN_DIR/dotnet" "$CURRENT_DIR/../../dotnet"
# # chmod +x "$CURRENT_DIR/../../dotnet"  # Make symlink executable (though it inherits from target)






# # ##########################
# # # install environment variables for everyone
# # sudo tee /etc/profile.d/dotnet.sh > /dev/null <<EOF
# # export DOTNET_ROOT=$DOTNET_BIN_DIR         # root of SDK install
# # export PATH=\$PATH:$DOTNET_BIN_DIR          # include dotnet binaries
# # EOF
# # sudo chmod +x /etc/profile.d/dotnet.sh        # make script executable
# # # Also create symlink for bash-specific startup (optional)
# # sudo ln -sf /etc/profile.d/dotnet.sh /etc/bash.bashrc.d/dotnet.sh 2>/dev/null || true
# # # Ensure it's loaded in non-login bash shells too
# # echo 'source /etc/profile.d/dotnet.sh' | sudo tee -a /etc/bash.bashrc > /dev/null
# # ##################################
# # # install environment variables manual loader script
# # # install environment variables for current user
# # if [ ! -f "$target_home/.bashrc" ]; then
# #     touch "$target_home/.bashrc"  # create .bashrc if it doesn't exist
# # fi
# # if ! grep -q "source /etc/profile.d/dotnet.sh" "$target_home/.bashrc"; then
# #     echo "source /etc/profile.d/dotnet.sh" >> "$target_home/.bashrc"
# # fi
# # ###########
# # # load environment variables for current user
# # # echo PATH IS=$PATH
# # source source /etc/profile.d/dotnet.sh
# # # echo PATH IS=$PATH

# pushd $DOTNET_BIN_DIR
# dotnet --version
# popd

# popd

# echo %%%%%%%%%%%%%%%  DOTNET 9 DONE: ${BASH_SOURCE[0]}
# echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%



# # Restore original shell settings
# set +euxo pipefail
# if [[ "$original_settings" =~ e ]]; then set -e; else set +e; fi
# if [[ "$original_settings" =~ u ]]; then set -u; else set +u; fi
# if [[ "$original_settings" =~ x ]]; then set -x; else set +x; fi
# if [[ "$original_settings" =~ o ]] && [[ "$original_settings" =~ pipefail ]]; then set -o pipefail; else set +o pipefail; fi

