import json
import os

PROFILES_FILE = "profiles.json"

def load_profiles():
    if not os.path.exists(PROFILES_FILE):
        return []
    try:
        with open(PROFILES_FILE, "r") as f:
            return json.load(f)
    except Exception:
        return []

def save_profiles(profiles):
    try:
        with open(PROFILES_FILE, "w") as f:
            json.dump(profiles, f, indent=4)
        return True
    except Exception:
        return False

def add_profile(name, host, share, username, password):
    profiles = load_profiles()
    profiles.append({
        "name": name,
        "host": host,
        "share": share,
        "username": username,
        "password": password
    })
    return save_profiles(profiles)

def delete_profile(index):
    profiles = load_profiles()
    if 0 <= index < len(profiles):
        profiles.pop(index)
        return save_profiles(profiles)
    return False
