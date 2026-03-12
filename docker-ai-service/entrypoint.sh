#!/bin/bash
set -e

# Only start SSH if authorized_keys exist (security: don't expose SSH without keys)
if [ -f /root/.ssh/authorized_keys ] && [ -s /root/.ssh/authorized_keys ]; then
    echo "SSH authorized_keys found - starting SSH service"

    # Generate SSH host keys if not present
    if [ ! -f /etc/ssh/ssh_host_rsa_key ]; then
        ssh-keygen -A
    fi

    # Ensure /run/sshd exists
    mkdir -p /run/sshd
    chmod 700 /root/.ssh
    chmod 600 /root/.ssh/authorized_keys

    # Start SSH service in background
    /usr/sbin/sshd -D &
else
    echo "No SSH authorized_keys found - SSH service disabled (mount keys to /root/.ssh/authorized_keys to enable)"
fi

# Start the main application
exec "$@"
