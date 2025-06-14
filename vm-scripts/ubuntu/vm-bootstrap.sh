#!/bin/bash
# .devcontainer/scripts/launch-helper.txt
# this script is what can be pasted into the VM bootstrap section of the devcontainer.json

# Save original shell settings
original_settings="$-"

# Consolidated bash options: exit on error/undefined vars, show commands, catch pipeline errors
set -euxo pipefail

# This script is intended to be run in a devcontainer or as part of a CI/CD pipeline.
# It installs necessary dependencies, sets up the .NET SDK, and prepares the environment for development.
# Get the directory where this script is located
CURRENT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# ubuntu version
# lsb_release -r -s
# arch
# python --version
# ls -lsa /usr/lib/python3/dist-packages/

original_user="$(whoami)"
echo "Original user: $original_user"

#update ubuntu
sudo apt update -y

echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
#install postgres
source $CURRENT_DIR/install-postgres.sh

echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
# install dotnet
source $CURRENT_DIR/install-dotnet.sh

# Fetch external submodules needed for the solution

git submodule update --init --recursive
# set +x
# sudo chown -R $original_user .
# sudo chmod -R 777 .
        



#restore projects
dotnet restore ./apps/cleartrix-dotnet/ --verbosity quiet

echo "%%%  Done with VM Bootstrap script! %%%"

# Restore original shell settings
set +euxo pipefail
if [[ "$original_settings" =~ e ]]; then set -e; else set +e; fi
if [[ "$original_settings" =~ u ]]; then set -u; else set +u; fi
if [[ "$original_settings" =~ x ]]; then set -x; else set +x; fi
if [[ "$original_settings" =~ o ]] && [[ "$original_settings" =~ pipefail ]]; then set -o pipefail; else set +o pipefail; fi

