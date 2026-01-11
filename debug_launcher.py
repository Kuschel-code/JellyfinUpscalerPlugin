import sys
import traceback

try:
    from app import App
    app = App()
    app.mainloop()
except Exception:
    with open("error_log.txt", "w") as f:
        traceback.print_exc(file=f)
    print("An error occurred. Check error_log.txt")
    sys.exit(1)
