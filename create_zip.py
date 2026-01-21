import zipfile
import os

dll_path = r'Jellyfin.Plugin.LanguageSelector\bin\Release\net8.0\Jellyfin.Plugin.LanguageSelector.dll'
zip_path = 'jellyfin-plugin-languageselector_1.0.1.0.zip'

with zipfile.ZipFile(zip_path, 'w') as z:
    z.write(dll_path, 'Jellyfin.Plugin.LanguageSelector.dll')

print(f'Created {zip_path}')
print(f'Size: {os.path.getsize(zip_path)} bytes')
