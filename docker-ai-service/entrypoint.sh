#!/bin/bash
set -e

# Generate SSH host keys if not present
if [ ! -f /etc/ssh/ssh_host_rsa_key ]; then
    ssh-keygen -A
fi

# Ensure /run/sshd exists
mkdir -p /run/sshd

# Setup root SSH directory if not exists
mkdir -p /root/.ssh
chmod 700 /root/.ssh

# Start SSH service in background
/usr/sbin/sshd -D &

# Start the main application
exec "$@"
