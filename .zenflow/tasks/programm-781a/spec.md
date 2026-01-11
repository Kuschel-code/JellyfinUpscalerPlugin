# Technical Specification - TrueNAS Multi-Account File Manager

## Technical Context
- **Language**: Python 3.12
- **GUI Framework**: `customtkinter` (Modern UI for Windows)
- **SMB Library**: `smbprotocol` (Supports SMB 2/3, pure Python, bypasses Windows Native SMB limitations)
- **Persistence**: Local JSON configuration for storing profiles (Encrypted password storage is ideal, but for MVP, simple base64 or obfuscation)

## Implementation Approach
The application will provide a graphical interface to manage multiple SMB connections simultaneously. Since Windows OS limits multiple connections to the same IP with different credentials, this Python app will use a dedicated SMB library (`smbprotocol`) that operates independently of the Windows Network Provider.

### Features:
- **Profile Management**: Save multiple server profiles (IP, Share, User, Password).
- **File Explorer**: Browse files and folders on the remote share.
- **File Transfer**: Upload from local to remote, Download from remote to local.
- **Dual-Pane Layout**: (Optional/Secondary) Similar to Total Commander for easy 1-to-1 transfer.

## Source Code Structure
- `app.py`: Main GUI application logic using `customtkinter`.
- `smb_handler.py`: Backend class to handle SMB operations (list, read, write).
- `models.py`: Data structures for Profiles and File items.
- `utils.py`: Helper functions for path handling and configuration.

## Data Model
```python
class Profile:
    name: str
    host: str
    share: str
    username: str
    password: str
```

## Verification Approach
- **Unit Tests**: Mock SMB connections to verify file listing and transfer logic.
- **Manual Verification**: Run the application and connect to a test SMB share (if available in the dev environment, otherwise using local simulations).
- **Linting**: `ruff` for code quality.
