# Spec and build

## Configuration
- **Artifacts Path**: {@artifacts_path} → `.zenflow/tasks/{task_id}`

---

## Agent Instructions

Ask the user questions when anything is unclear or needs their input. This includes:
- Ambiguous or incomplete requirements
- Technical decisions that affect architecture or user experience
- Trade-offs that require business context

Do not make assumptions on important decisions — get clarification first.

---

## Workflow Steps

### [x] Step: Technical Specification

Assess the task's difficulty: **hard** (due to SMB complexities and UI requirements).

### [x] Step: Project Setup
- Initialize Python environment.
- Install dependencies: `customtkinter`, `smbprotocol`.
- Set up project structure.

### [x] Step: SMB Backend Implementation
- Create `smb_handler.py` to handle connections and file operations.
- Implement session management to support multiple accounts.

### [x] Step: GUI - Profile Management
- Create UI for adding/editing/deleting server profiles.
- Implement profile persistence (JSON).

### [x] Step: GUI - File Explorer
- Implement file listing view.
- Add navigation (back, forward, home).
- Support basic file operations (upload, download, delete).

### [x] Step: Verification & Finalization
- Run linters.
- Manual verification of connectivity.
- Create final report.

