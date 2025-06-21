#!/bin/bash
# installs postgresql 16.x for ubuntu

set -x # show all commands
set -e  # exit on error
set -o pipefail  # exit on error in a pipeline


echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%
echo %%%%%%%%%%%%%%%  START: ${BASH_SOURCE[0]}

# ## instructions from: https://github.com/nodesource/distributions?tab=readme-ov-file#installation-instructions-deb
# sudo apt-get install -y curl
# curl -fsSL https://deb.nodesource.com/setup_24.x -o nodesource_setup.sh
# sudo -E bash nodesource_setup.sh
# rm nodesource_setup.sh
# sudo apt-get install -y nodejs
# node -v   #verify install


npm install -g @anthropic-ai/claude-code


sudo chmod 777 -R .

# install az cli
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash

# install 


# then claude: https://docs.anthropic.com/en/docs/claude-code/getting-started
# sudo npm install -g @anthropic-ai/claude-code



# claude code monitor
# https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor
# https://news.ycombinator.com/item?id=44317012
sudo apt install -y pip pipx python3-venv
pushd /workspaces
git clone https://github.com/Maciek-roboblog/Claude-Code-Usage-Monitor.git  # fetches entire history
cd Claude-Code-Usage-Monitor
git checkout 4aa84a96f2e634e231e9538d83b59a6bcedf3297 # moves HEAD into detached‐HEAD at that SHA
python3 -m venv venv
source venv/bin/activate
pip install pytz
chmod +x ccusage_monitor.py
# # run monitor
# python ccusage_monitor.py
popd

# sudo apt update -y

# # postgres
# sudo apt install -y postgresql-16 postgresql-client-16 
# sudo service postgresql start 


# # sudo bash << EOF   # start root shell and feed commands until EOF
# # # query pg_roles; if exit code 0, role exists
# # if su - postgres -c "psql -tAc \"SELECT 1 FROM pg_roles WHERE rolname='${TARGET_DB_USER}'\""; then
# #     echo "existing role → update password"
# #     su - postgres -c "psql -c \"ALTER ROLE ${TARGET_DB_USER} WITH PASSWORD '${TARGET_DB_PASS}';\""  # update pw only if exists
# # else
# #     echo "role missing → create with password"
# #     su - postgres -c "psql -c \"CREATE ROLE ${TARGET_DB_USER} LOGIN SUPERUSER PASSWORD '${TARGET_DB_PASS}';\""  # create new role
# # fi
# # EOF

# sudo bash << EOF   # start root shell and feed commands until EOF
# # set postgres role password once
# sudo -u postgres psql -c "ALTER USER postgres WITH PASSWORD 'postgres';"
# # switch peer → md5 in pg_hba.conf, then reload cluster
# sudo -u postgres pg_ctlcluster 16 main reload     # apply auth change
# EOF



echo %%%%%%%%%%%%%%%  DONE: ${BASH_SOURCE[0]}
echo %%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%%





# # --- PostgreSQL Setup ---
# echo "Starting PostgreSQL setup with hardcoded values..."
# echo "PostgreSQL target configuration: DB_NAME=${TARGET_DB_NAME}, DB_USER=${TARGET_DB_USER}"

# # Start PostgreSQL service (systemd check for broader compatibility)
# echo "Starting PostgreSQL service..."
# # using systemd if available
# if command -v systemctl &> /dev/null && systemctl list-units --type=service --all | grep -Fq 'postgresql.service'; then
#     if [ "$(id -u)" -ne 0 ]; then
#         sudo systemctl start postgresql   # start via systemd as non-root :contentReference[oaicite:1]{index=1}
#     else
#         systemctl start postgresql
#     fi
# else
#     if [ "$(id -u)" -ne 0 ]; then
#         sudo service postgresql start      # fallback to SysV init on older Ubuntu :contentReference[oaicite:2]{index=2}
#     else
#         service postgresql start
#     fi
# fi

# # Wait for PostgreSQL to be ready
# RETRY_COUNT=0
# MAX_RETRIES=10
# RETRY_DELAY=3
# until pg_isready -q || [ ${RETRY_COUNT} -ge ${MAX_RETRIES} ]; do
#     echo "Waiting for PostgreSQL to become ready... ($((${RETRY_COUNT}+1))/${MAX_RETRIES})"
#     sleep ${RETRY_DELAY}
#     RETRY_COUNT=$((RETRY_COUNT+1))
# done

# if ! pg_isready -q; then
#     echo "PostgreSQL is not ready after ${MAX_RETRIES} attempts. DB setup cannot continue."
#     # If this script is sourced, return will stop this script part. If run directly, exit.
#     # Assuming it might be sourced in some contexts of devcontainer.
#     return 1
# fi
# echo "PostgreSQL service started and ready."

# #############################################################

# sudo bash << EOF   # start root shell and feed commands until EOF
# # query pg_roles; if exit code 0, role exists
# if su - postgres -c "psql -tAc \"SELECT 1 FROM pg_roles WHERE rolname='${TARGET_DB_USER}'\""; then
#     echo "existing role → update password"
#     su - postgres -c "psql -c \"ALTER ROLE ${TARGET_DB_USER} WITH PASSWORD '${TARGET_DB_PASS}';\""  # update pw only if exists
# else
#     echo "role missing → create with password"
#     su - postgres -c "psql -c \"CREATE ROLE ${TARGET_DB_USER} LOGIN SUPERUSER PASSWORD '${TARGET_DB_PASS}';\""  # create new role
# fi
# EOF
# #################################################################

# # # Get current user name
# # CURRENT_USER=$(whoami)
# # echo "Setting up PostgreSQL superuser for current user: ${CURRENT_USER}"

# # # Create superuser role for current user using peer authentication
# # sudo -u postgres createuser --superuser "${CURRENT_USER}" 2>/dev/null || echo "User ${CURRENT_USER} already exists or creation failed"

# # # Database and User creation commands
# # echo "Creating PostgreSQL database (${TARGET_DB_NAME}) and user (${TARGET_DB_USER})..."

# # # Now we can use psql directly without sudo or passwords (peer authentication)
# # echo "Setting password for user ${TARGET_DB_USER}..."
# # psql -d postgres -c "ALTER ROLE ${TARGET_DB_USER} WITH LOGIN PASSWORD '${TARGET_DB_PASS}';"
# # if [ $? -ne 0 ]; then
# #     echo "Warning: Failed to ALTER ROLE for ${TARGET_DB_USER}. Attempting CREATE ROLE..."
# #     psql -d postgres -c "CREATE ROLE ${TARGET_DB_USER} WITH LOGIN PASSWORD '${TARGET_DB_PASS}';"
# #     if [ $? -ne 0 ]; then echo "Error: Failed to create or alter role ${TARGET_DB_USER}. Manual check required."; fi
# # fi

# # # Create database if it doesn't exist, owned by TARGET_DB_USER
# # DB_EXISTS=$(psql -d postgres -tAc "SELECT 1 FROM pg_database WHERE datname='${TARGET_DB_NAME}';")
# # if [ "${DB_EXISTS}" != "1" ]; then
# #     echo "Database ${TARGET_DB_NAME} does not exist. Creating..."
# #     psql -d postgres -c "CREATE DATABASE ${TARGET_DB_NAME} OWNER ${TARGET_DB_USER};"
# #     if [ $? -ne 0 ]; then echo "Warning: Error creating database ${TARGET_DB_NAME}."; fi
# # else
# #     echo "Database ${TARGET_DB_NAME} already exists."
# # fi

# # # Grant all privileges on the new database to the new user
# # echo "Granting privileges on database ${TARGET_DB_NAME} to ${TARGET_DB_USER}..."
# # psql -d postgres -c "GRANT ALL PRIVILEGES ON DATABASE ${TARGET_DB_NAME} TO ${TARGET_DB_USER};"
# # if [ $? -ne 0 ]; then echo "Warning: Failed to grant privileges on database ${TARGET_DB_NAME} to ${TARGET_DB_USER}."; fi

# # # Grant privileges on the public schema
# # echo "Granting privileges on public schema of ${TARGET_DB_NAME} to ${TARGET_DB_USER}..."
# # psql -d "${TARGET_DB_NAME}" -c "GRANT ALL ON SCHEMA public TO ${TARGET_DB_USER};"
# # if [ $? -ne 0 ]; then echo "Warning: Failed to grant privileges on public schema of ${TARGET_DB_NAME} to ${TARGET_DB_USER}."; fi

# echo "PostgreSQL database and user setup complete."
# # --- End PostgreSQL Setup ---



# # Install Node dependencies from the repository root
# # if [ -f package-lock.json ]; then
# #    disable npm for now
# #    npm ci
# # fi

