import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk
import os
from smb_handler import SMBHandler
import config
import shutil

ctk.set_appearance_mode("System")
ctk.set_default_color_theme("blue")

class FileBrowser(ctk.CTkFrame):
    def __init__(self, master, app, side_name, **kwargs):
        super().__init__(master, **kwargs)
        self.app = app
        self.side_name = side_name
        self.current_handler = None
        self.current_share = ""
        self.current_path = ""

        self.grid_columnconfigure(0, weight=1)
        self.grid_rowconfigure(2, weight=1)

        self.title_label = ctk.CTkLabel(self, text=f"Pane: {side_name}", font=ctk.CTkFont(size=16, weight="bold"))
        self.title_label.grid(row=0, column=0, padx=10, pady=5, sticky="w")

        self.path_label = ctk.CTkLabel(self, text="Not connected", font=ctk.CTkFont(size=12))
        self.path_label.grid(row=1, column=0, padx=10, pady=2, sticky="w")

        # File List
        self.file_tree = tk.Listbox(self, bg="#333333", fg="white", font=("Segoe UI", 10), selectmode=tk.SINGLE, borderwidth=0, highlightthickness=1)
        self.file_tree.grid(row=2, column=0, sticky="nsew", padx=10, pady=5)
        self.file_tree.bind("<Double-1>", self.on_item_double_click)
        
        # Drag and Drop bindings
        self.file_tree.bind("<ButtonPress-1>", self.on_drag_start)
        self.file_tree.bind("<B1-Motion>", self.on_drag_motion)
        self.file_tree.bind("<ButtonRelease-1>", self.on_drag_release)

        # Controls
        self.controls_frame = ctk.CTkFrame(self)
        self.controls_frame.grid(row=3, column=0, sticky="ew", padx=10, pady=5)
        
        self.back_button = ctk.CTkButton(self.controls_frame, text="Back", width=60, command=self.go_back)
        self.back_button.pack(side="left", padx=2)

        self.refresh_button = ctk.CTkButton(self.controls_frame, text="Refresh", width=60, command=self.refresh_files)
        self.refresh_button.pack(side="left", padx=2)

        self.upload_button = ctk.CTkButton(self.controls_frame, text="Up", width=40, command=self.upload_file)
        self.upload_button.pack(side="left", padx=2)

        self.download_button = ctk.CTkButton(self.controls_frame, text="Down", width=40, command=self.download_file)
        self.download_button.pack(side="left", padx=2)

        self.delete_button = ctk.CTkButton(self.controls_frame, text="Del", width=40, fg_color="red", command=self.delete_file)
        self.delete_button.pack(side="left", padx=2)

    def connect(self, profile):
        self.current_handler = SMBHandler(profile["host"], profile["username"], profile["password"])
        success, msg = self.current_handler.connect()
        if success:
            self.current_share = profile["share"]
            self.current_path = ""
            self.refresh_files()
            self.title_label.configure(text=f"{self.side_name}: {profile['name']}")
        else:
            messagebox.showerror("Error", f"Connection failed: {msg}")

    def refresh_files(self):
        if not self.current_handler:
            return

        self.file_tree.delete(0, tk.END)
        items = self.current_handler.list_files(self.current_share, self.current_path)
        
        if self.current_path != "":
            self.file_tree.insert(tk.END, "[DIR] ..")

        for item in items:
            prefix = "[DIR] " if item["is_dir"] else "      "
            self.file_tree.insert(tk.END, f"{prefix}{item['name']}")
        
        self.path_label.configure(text=f"Path: {self.current_share}/{self.current_path}")

    def on_item_double_click(self, event):
        selection = self.file_tree.curselection()
        if not selection:
            return
        
        item_text = self.file_tree.get(selection[0])
        name = item_text[6:] if item_text.startswith("[DIR] ") else item_text.strip()

        if item_text.startswith("[DIR] "):
            if name == "..":
                self.go_back()
            else:
                self.current_path = os.path.join(self.current_path, name).replace("\\", "/")
                self.refresh_files()

    def go_back(self):
        if self.current_path == "":
            return
        parts = self.current_path.strip("/").split("/")
        if len(parts) <= 1:
            self.current_path = ""
        else:
            self.current_path = "/".join(parts[:-1])
        self.refresh_files()

    def upload_file(self):
        if not self.current_handler:
            return
        file_path = filedialog.askopenfilename()
        if file_path:
            filename = os.path.basename(file_path)
            remote_path = os.path.join(self.current_path, filename).replace("\\", "/")
            success, msg = self.current_handler.upload_file(file_path, self.current_share, remote_path)
            if success:
                self.refresh_files()
            else:
                messagebox.showerror("Error", msg)

    def download_file(self):
        if not self.current_handler:
            return
        selection = self.file_tree.curselection()
        if not selection:
            return
        
        item_text = self.file_tree.get(selection[0])
        if item_text.startswith("[DIR] "):
            return
        
        name = item_text.strip()
        remote_path = os.path.join(self.current_path, name).replace("\\", "/")
        local_path = filedialog.asksaveasfilename(initialfile=name)
        if local_path:
            success, msg = self.current_handler.download_file(self.current_share, remote_path, local_path)
            if success:
                messagebox.showinfo("Success", "File downloaded")
            else:
                messagebox.showerror("Error", msg)

    def delete_file(self):
        if not self.current_handler:
            return
        selection = self.file_tree.curselection()
        if not selection:
            return
        
        item_text = self.file_tree.get(selection[0])
        name = item_text[6:] if item_text.startswith("[DIR] ") else item_text.strip()
        
        if name == "..":
            return

        if messagebox.askyesno("Confirm", f"Delete {name}?"):
            remote_path = os.path.join(self.current_path, name).replace("\\", "/")
            if item_text.startswith("[DIR] "):
                success, msg = self.current_handler.delete_directory(self.current_share, remote_path)
            else:
                success, msg = self.current_handler.delete_file(self.current_share, remote_path)
            
            if success:
                self.refresh_files()
            else:
                messagebox.showerror("Error", msg)

    def on_drag_start(self, event):
        index = self.file_tree.nearest(event.y)
        if index < 0: return
        self.file_tree.selection_clear(0, tk.END)
        self.file_tree.selection_set(index)
        self._drag_data = {"item": self.file_tree.get(index)}
        self.file_tree.config(cursor="hand2")

    def on_drag_motion(self, event):
        pass

    def on_drag_release(self, event):
        self.file_tree.config(cursor="")
        if not hasattr(self, "_drag_data"):
            return
        
        x, y = event.x_root, event.y_root
        target_widget = self.winfo_containing(x, y)
        
        other_pane = self.app.pane_right if self == self.app.pane_left else self.app.pane_left
        
        # Check if dropped on other pane or its child widgets
        if target_widget and (target_widget == other_pane.file_tree or target_widget.master == other_pane):
            self.app.copy_item(self, other_pane, self._drag_data["item"])
        
        del self._drag_data

class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("TrueNAS Manager - Dual Pane")
        self.geometry("1200x700")

        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.profiles = config.load_profiles()

        # Sidebar
        self.sidebar_frame = ctk.CTkFrame(self, width=200, corner_radius=0)
        self.sidebar_frame.grid(row=0, column=0, sticky="nsew")
        self.sidebar_frame.grid_rowconfigure(4, weight=1)

        self.logo_label = ctk.CTkLabel(self.sidebar_frame, text="Profiles", font=ctk.CTkFont(size=20, weight="bold"))
        self.logo_label.grid(row=0, column=0, padx=20, pady=(20, 10))

        self.profile_listbox = tk.Listbox(self.sidebar_frame, bg="#2b2b2b", fg="white", borderwidth=0, highlightthickness=0)
        self.profile_listbox.grid(row=1, column=0, padx=10, pady=10, sticky="nsew")
        self.update_profile_listbox()

        self.add_button = ctk.CTkButton(self.sidebar_frame, text="Add Profile", command=self.add_profile_dialog)
        self.add_button.grid(row=2, column=0, padx=20, pady=5)

        self.delete_button = ctk.CTkButton(self.sidebar_frame, text="Delete Profile", command=self.delete_profile)
        self.delete_button.grid(row=3, column=0, padx=20, pady=5)

        self.connect_left_button = ctk.CTkButton(self.sidebar_frame, text="Connect Left", command=lambda: self.connect_pane("left"))
        self.connect_left_button.grid(row=5, column=0, padx=20, pady=5)

        self.connect_right_button = ctk.CTkButton(self.sidebar_frame, text="Connect Right", command=lambda: self.connect_pane("right"))
        self.connect_right_button.grid(row=6, column=0, padx=20, pady=(5, 20))

        # Main View (Dual Pane)
        self.main_frame = ctk.CTkFrame(self, corner_radius=0)
        self.main_frame.grid(row=0, column=1, sticky="nsew", padx=10, pady=10)
        self.main_frame.grid_columnconfigure(0, weight=1)
        self.main_frame.grid_columnconfigure(1, weight=1)
        self.main_frame.grid_rowconfigure(0, weight=1)

        self.pane_left = FileBrowser(self.main_frame, self, "Left")
        self.pane_left.grid(row=0, column=0, sticky="nsew", padx=5, pady=5)

        self.pane_right = FileBrowser(self.main_frame, self, "Right")
        self.pane_right.grid(row=0, column=1, sticky="nsew", padx=5, pady=5)

    def update_profile_listbox(self):
        self.profile_listbox.delete(0, tk.END)
        self.profiles = config.load_profiles()
        for p in self.profiles:
            self.profile_listbox.insert(tk.END, p["name"])

    def add_profile_dialog(self):
        dialog = ProfileDialog(self)
        self.wait_window(dialog)
        self.update_profile_listbox()

    def delete_profile(self):
        selection = self.profile_listbox.curselection()
        if selection:
            idx = selection[0]
            if messagebox.askyesno("Confirm", f"Delete profile '{self.profiles[idx]['name']}'?"):
                config.delete_profile(idx)
                self.update_profile_listbox()

    def connect_pane(self, side):
        selection = self.profile_listbox.curselection()
        if not selection:
            messagebox.showwarning("Warning", "Select a profile first")
            return

        p = self.profiles[selection[0]]
        if side == "left":
            self.pane_left.connect(p)
        else:
            self.pane_right.connect(p)

    def copy_item(self, source_pane, target_pane, item_text):
        if not source_pane.current_handler or not target_pane.current_handler:
            messagebox.showwarning("Warning", "Both panes must be connected")
            return
        
        if item_text.startswith("[DIR] "):
            if item_text[6:] == "..":
                return
            messagebox.showinfo("Info", "Directory copy not yet implemented.")
            return

        filename = item_text.strip()
        source_rel_path = os.path.join(source_pane.current_path, filename).replace("\\", "/")
        target_rel_path = os.path.join(target_pane.current_path, filename).replace("\\", "/")

        temp_dir = os.path.join(os.environ.get("TEMP", "."), "TrueNAS_Manager")
        os.makedirs(temp_dir, exist_ok=True)
        temp_path = os.path.join(temp_dir, filename)

        try:
            # Download from source
            success, msg = source_pane.current_handler.download_file(source_pane.current_share, source_rel_path, temp_path)
            if not success:
                messagebox.showerror("Error", f"Failed to download: {msg}")
                return

            # Upload to target
            success, msg = target_pane.current_handler.upload_file(temp_path, target_pane.current_share, target_rel_path)
            if not success:
                messagebox.showerror("Error", f"Failed to upload: {msg}")
            else:
                target_pane.refresh_files()
            
            if os.path.exists(temp_path):
                os.remove(temp_path)
        except Exception as e:
            messagebox.showerror("Error", str(e))

class ProfileDialog(ctk.CTkToplevel):
    def __init__(self, parent):
        super().__init__(parent)
        self.title("Add Profile")
        self.geometry("400x450")
        self.transient(parent)
        self.grab_set()

        ctk.CTkLabel(self, text="Profile Name:").pack(pady=(20, 0))
        self.name_entry = ctk.CTkEntry(self, width=300)
        self.name_entry.pack(pady=5)

        ctk.CTkLabel(self, text="Host (IP):").pack()
        self.host_entry = ctk.CTkEntry(self, width=300)
        self.host_entry.pack(pady=5)

        ctk.CTkLabel(self, text="Share Name:").pack()
        self.share_entry = ctk.CTkEntry(self, width=300)
        self.share_entry.pack(pady=5)

        ctk.CTkLabel(self, text="Username:").pack()
        self.user_entry = ctk.CTkEntry(self, width=300)
        self.user_entry.pack(pady=5)

        ctk.CTkLabel(self, text="Password:").pack()
        self.pass_entry = ctk.CTkEntry(self, width=300, show="*")
        self.pass_entry.pack(pady=5)

        self.save_button = ctk.CTkButton(self, text="Save", command=self.save)
        self.save_button.pack(pady=20)

    def save(self):
        name = self.name_entry.get()
        host = self.host_entry.get()
        share = self.share_entry.get()
        user = self.user_entry.get()
        password = self.pass_entry.get()

        if all([name, host, share, user, password]):
            config.add_profile(name, host, share, user, password)
            self.destroy()
        else:
            messagebox.showwarning("Warning", "All fields are required")

if __name__ == "__main__":
    app = App()
    app.mainloop()
