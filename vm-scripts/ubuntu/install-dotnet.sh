#!/bin/bash
# installs dotnet 9.x for ubuntu

# Save original shell settings
original_settings="$-"

# Consolidated bash options: exit on error/undefined vars, show commands, catch pipeline errors
set -euxo pipefail



echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
echo %%%%%%%%%%%%%%%  DOTNET 9 START: ${BASH_SOURCE[0]}

## install dotnet from repository, doesn't work on Jules because of Python error.
sudo apt-get update -y                             # update package lists
sudo apt-get install -y software-properties-common   # provides add-apt-repository  
#sudo add-apt-repository ppa:dotnet/backports -y     # registers Canonical’s .NET backports repo.  source: https://pupli.net/2025/03/12/install-net-sdk-9-0-on-ubuntu-22-04/
sudo python3.12 /usr/bin/add-apt-repository ppa:dotnet/backports -y     # jules workaround for python error in add-apt-repository
sudo apt-get update -y                               # include newly added dotnet/backports packages 
sudo apt-get install -y dotnet-sdk-9.0            # installs .NET 9 from the backports PPA
dotnet --version

# # set envvars needed
# export DOTNET_ENVIRONMENT=Test
# export ASPNETCORE_ENVIRONMENT=Test
# # needed for microsoft logging failure in VM's
# export MSBUILDTERMINALLOGGER=false
# export COLUMNS=120
# # no telemetry
# export DOTNET_NOLOGO=true
# export DOTNET_CLI_TELEMETRY_OPTOUT=true

# CURRENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"   # absolute dir of script
# DOTNET_BIN_DIR="$(realpath "$CURRENT_DIR/../../.dotnet")"  # resolve .. and symlinks
# original_user="$(whoami)"
# # ensure we update the invoking user's bashrc, even when running with sudo
# target_user="${SUDO_USER:-${USER:-root}}"
# target_home="$(eval echo "~$target_user")"

# # if dotnet --version is already installed, skip installation
# if command -v dotnet &> /dev/null; then
#     echo "dotnet is already installed, skipping installation."
#     dotnet --version
#     return 0
# fi

# # if $DOTNET_BIN_DIR/dotnet-env.sh exists, source it and exit
# if [ -f "$DOTNET_BIN_DIR/dotnet-env.sh" ]; then
#     echo "dotnet-env.sh already exists, sourcing it."
#     source "$DOTNET_BIN_DIR/dotnet-env.sh"

#     # also add to ~/.bashrc if not already there
#     if ! grep -q "source $DOTNET_BIN_DIR/dotnet-env.sh" ~/.bashrc; then
#         echo "source $DOTNET_BIN_DIR/dotnet-env.sh" >> ~/.bashrc
#     fi
#     echo "Environment variables sourced from $DOTNET_BIN_DIR/dotnet-env.sh"
#     echo "Current .NET version:"
#     dotnet --version
#     return 0
# fi





# mkdir -p $DOTNET_BIN_DIR

# pushd $DOTNET_BIN_DIR



# # install dotnet manually: https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/sdk-9.0.300-linux-x64-binaries
# # check if .gz already exists, if so, skip download
# if [ -f "dotnet-sdk-9.0.300-linux-x64.tar.gz" ]; then
#     echo "dotnet-sdk-9.0.300-linux-x64.tar.gz already exists, skipping download."
# else
#     echo "Downloading dotnet-sdk-9.0.300-linux-x64.tar.gz..."
#     wget https://builds.dotnet.microsoft.com/dotnet/Sdk/9.0.300/dotnet-sdk-9.0.300-linux-x64.tar.gz    
#     sudo apt update -y
# fi

# #sudo mkdir -p /usr/share/dotnet 
# sudo tar zxf dotnet-sdk-9.0.300-linux-x64.tar.gz -C .




# ##################################
# # install environment variables manual loader script

# sudo tee "$DOTNET_BIN_DIR/dotnet-env.sh" > /dev/null <<EOF
# export DOTNET_ROOT=$DOTNET_BIN_DIR         # root of SDK install
# export PATH=\$PATH:$DOTNET_BIN_DIR          # include dotnet binaries
# EOF
# sudo chmod +x $DOTNET_BIN_DIR/dotnet-env.sh        # make script executable

# # install environment variables for current user
# if [ ! -f "$target_home/.bashrc" ]; then
#     touch "$target_home/.bashrc"  # create .bashrc if it doesn't exist
# fi
# if ! grep -q "source $DOTNET_BIN_DIR/dotnet-env.sh" "$target_home/.bashrc"; then
#     echo "source $DOTNET_BIN_DIR/dotnet-env.sh" >> "$target_home/.bashrc"
# fi

# ######################
# # make everything accessible by anyone
# # sudo chown -R $target_user $DOTNET_BIN_DIR
# # sudo chmod -R 777 $DOTNET_BIN_DIR


# ###########
# # load environment variables for current user
# # echo PATH IS=$PATH
# source $DOTNET_BIN_DIR/dotnet-env.sh 
# # echo PATH IS=$PATH

# dotnet --version




# ##########################
# # install environment variables for everyone
# sudo tee /etc/profile.d/dotnet.sh > /dev/null <<EOF
# export DOTNET_ROOT=$DOTNET_BIN_DIR         # root of SDK install
# export PATH=\$PATH:$DOTNET_BIN_DIR          # include dotnet binaries
# EOF
# sudo chmod +x /etc/profile.d/dotnet.sh        # make script executable

# # Also create symlink for bash-specific startup (optional)
# sudo ln -sf /etc/profile.d/dotnet.sh /etc/bash.bashrc.d/dotnet.sh 2>/dev/null || true
# # Ensure it's loaded in non-login bash shells too
# echo 'source /etc/profile.d/dotnet.sh' | sudo tee -a /etc/bash.bashrc > /dev/null




# popd

echo %%%%%%%%%%%%%%%  DOTNET 9 DONE: ${BASH_SOURCE[0]}
echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%



# Restore original shell settings
set +euxo pipefail
if [[ "$original_settings" =~ e ]]; then set -e; else set +e; fi
if [[ "$original_settings" =~ u ]]; then set -u; else set +u; fi
if [[ "$original_settings" =~ x ]]; then set -x; else set +x; fi
if [[ "$original_settings" =~ o ]] && [[ "$original_settings" =~ pipefail ]]; then set -o pipefail; else set +o pipefail; fi

