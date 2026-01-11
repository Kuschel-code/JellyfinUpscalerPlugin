import shutil
from smbclient import (
    register_session,
    mkdir,
    remove,
    rmdir,
    scandir,
    open_file
)

class SMBHandler:
    def __init__(self, host, username, password, port=445):
        self.host = host
        self.username = username
        self.password = password
        self.port = port
        self.session_registered = False

    def connect(self):
        try:
            # register_session is a convenience function in smbclient
            # It handles the connection and session creation.
            # We can use multiple sessions for the same host by providing the username/password.
            register_session(self.host, username=self.username, password=self.password, port=self.port)
            self.session_registered = True
            return True, "Connected"
        except Exception as e:
            return False, str(e)

    def get_full_path(self, share, path=""):
        # smbclient expects paths like \\host\share\path
        path = path.replace("/", "\\")
        if path.startswith("\\"):
            path = path[1:]
        return f"\\\\{self.host}\\{share}\\{path}"

    def list_files(self, share, path=""):
        full_path = self.get_full_path(share, path)
        try:
            items = []
            for entry in scandir(full_path):
                items.append({
                    "name": entry.name,
                    "is_dir": entry.is_dir(),
                    "size": entry.stat().st_size,
                    "mtime": entry.stat().st_mtime
                })
            return items
        except Exception as e:
            print(f"Error listing files: {e}")
            return []

    def download_file(self, share, remote_path, local_path):
        full_remote_path = self.get_full_path(share, remote_path)
        try:
            with open_file(full_remote_path, mode="rb") as remote_file:
                with open(local_path, mode="wb") as local_file:
                    shutil.copyfileobj(remote_file, local_file)
            return True, "Download successful"
        except Exception as e:
            return False, str(e)

    def upload_file(self, local_path, share, remote_path):
        full_remote_path = self.get_full_path(share, remote_path)
        try:
            with open(local_path, mode="rb") as local_file:
                with open_file(full_remote_path, mode="wb") as remote_file:
                    shutil.copyfileobj(local_file, remote_file)
            return True, "Upload successful"
        except Exception as e:
            return False, str(e)

    def delete_file(self, share, path):
        full_path = self.get_full_path(share, path)
        try:
            remove(full_path)
            return True, "File deleted"
        except Exception as e:
            return False, str(e)

    def delete_directory(self, share, path):
        full_path = self.get_full_path(share, path)
        try:
            rmdir(full_path)
            return True, "Directory deleted"
        except Exception as e:
            return False, str(e)

    def create_directory(self, share, path):
        full_path = self.get_full_path(share, path)
        try:
            mkdir(full_path)
            return True, "Directory created"
        except Exception as e:
            return False, str(e)
