import tkinter as tk
from tkinter import filedialog, messagebox
import customtkinter as ctk
import os
from smb_handler import SMBHandler
import config

ctk.set_appearance_mode("System")
ctk.set_default_color_theme("blue")

class App(ctk.CTk):
    def __init__(self):
        super().__init__()

        self.title("TrueNAS Multi-Account Manager")
        self.geometry("1000x600")

        self.grid_columnconfigure(1, weight=1)
        self.grid_rowconfigure(0, weight=1)

        self.current_handler = None
        self.current_share = ""
        self.current_path = ""
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
        self.add_button.grid(row=2, column=0, padx=20, pady=10)

        self.delete_button = ctk.CTkButton(self.sidebar_frame, text="Delete Profile", command=self.delete_profile)
        self.delete_button.grid(row=3, column=0, padx=20, pady=10)

        self.connect_button = ctk.CTkButton(self.sidebar_frame, text="Connect", command=self.connect_to_profile)
        self.connect_button.grid(row=5, column=0, padx=20, pady=20)

        # Main View
        self.main_frame = ctk.CTkFrame(self, corner_radius=0)
        self.main_frame.grid(row=0, column=1, sticky="nsew", padx=10, pady=10)
        self.main_frame.grid_columnconfigure(0, weight=1)
        self.main_frame.grid_rowconfigure(1, weight=1)

        self.path_label = ctk.CTkLabel(self.main_frame, text="Path: /", font=ctk.CTkFont(size=14))
        self.path_label.grid(row=0, column=0, padx=10, pady=10, sticky="w")

        # File List
        self.file_list_frame = ctk.CTkFrame(self.main_frame)
        self.file_list_frame.grid(row=1, column=0, sticky="nsew", padx=10, pady=10)
        self.file_list_frame.grid_columnconfigure(0, weight=1)
        self.file_list_frame.grid_rowconfigure(0, weight=1)

        self.file_tree = tk.Listbox(self.file_list_frame, bg="#333333", fg="white", font=("Segoe UI", 10))
        self.file_tree.grid(row=0, column=0, sticky="nsew")
        self.file_tree.bind("<Double-1>", self.on_item_double_click)

        # Controls
        self.controls_frame = ctk.CTkFrame(self.main_frame)
        self.controls_frame.grid(row=2, column=0, sticky="ew", padx=10, pady=10)

        self.back_button = ctk.CTkButton(self.controls_frame, text="Back", width=60, command=self.go_back)
        self.back_button.grid(row=0, column=0, padx=5)

        self.refresh_button = ctk.CTkButton(self.controls_frame, text="Refresh", width=60, command=self.refresh_files)
        self.refresh_button.grid(row=0, column=1, padx=5)

        self.upload_button = ctk.CTkButton(self.controls_frame, text="Upload", width=100, command=self.upload_file)
        self.upload_button.grid(row=0, column=2, padx=5)

        self.download_button = ctk.CTkButton(self.controls_frame, text="Download", width=100, command=self.download_file)
        self.download_button.grid(row=0, column=3, padx=5)

        self.delete_file_button = ctk.CTkButton(self.controls_frame, text="Delete", width=100, fg_color="red", command=self.delete_file)
        self.delete_file_button.grid(row=0, column=4, padx=5)

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

    def connect_to_profile(self):
        selection = self.profile_listbox.curselection()
        if not selection:
            messagebox.showwarning("Warning", "Select a profile first")
            return

        p = self.profiles[selection[0]]
        self.current_handler = SMBHandler(p["host"], p["username"], p["password"])
        success, msg = self.current_handler.connect()
        if success:
            self.current_share = p["share"]
            self.current_path = ""
            self.refresh_files()
        else:
            messagebox.showerror("Error", f"Connection failed: {msg}")

    def refresh_files(self):
        if not self.current_handler:
            return

        self.file_tree.delete(0, tk.END)
        items = self.current_handler.list_files(self.current_share, self.current_path)
        
        # Add ".." for navigation if not at root
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
