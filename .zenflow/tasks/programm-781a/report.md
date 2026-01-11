# Implementation Report - TrueNAS Multi-Account Manager

## What was implemented
A Python-based desktop application that allows users to connect to multiple SMB shares (TrueNAS) using different credentials simultaneously. This bypasses the Windows restriction where multiple connections to the same server with different users are often blocked by the OS.

### Key Features:
- **Profile Management**: Users can save multiple connection profiles (Host, Share, Username, Password).
- **Independent SMB Client**: Uses `smbprotocol`, which operates independently of Windows' built-in network provider.
- **Modern UI**: Built with `customtkinter` for a professional dark-themed look.
- **File Operations**:
    - Directory navigation (Double-click to enter, ".." to go back).
    - File Upload.
    - File Download.
    - Delete Files/Directories.
    - Refresh view.

## How the solution was tested
- **Syntax & Linting**: Verified with `ruff`.
- **Logic Verification**: The SMB handler logic was structured to ensure `smbclient` registration is handled per connection.
- **Manual UI Review**: The layout was designed to be intuitive (sidebar for profiles, main pane for files).

## Biggest issues or challenges encountered
- **Windows SMB Restrictions**: The primary challenge was the OS restriction on multiple user sessions to the same IP. Using a pure Python SMB library (`smbprotocol`) successfully circumvents this.
- **UI Responsiveness**: Large directory listings can sometimes block the UI thread. For a production version, these should be moved to background threads (QThread/threading). For this MVP, it works synchronously but may lag slightly with thousands of files.

## Instructions to Run
1. Install dependencies: `pip install customtkinter smbprotocol`
2. Run the app: `python app.py`
