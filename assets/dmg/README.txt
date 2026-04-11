=== LocalSynapse — Installation Guide ===

1. INSTALL
   Drag "LocalSynapse" to "Applications" folder.

2. FIRST LAUNCH (macOS security)
   macOS may show a warning because this app is not
   signed with an Apple certificate. This is a standard
   security warning, not a problem with the app.

   Option A — Terminal method (recommended, all macOS versions):
     1) Open Terminal (Applications > Utilities > Terminal)
     2) Paste this command and press Enter:

        xattr -cr /Applications/LocalSynapse.app

     3) Double-click the app to launch normally.

   Option B — System Settings method:
     1) Double-click LocalSynapse. macOS will block it.
     2) Click "Done" on the warning dialog.
     3) Open System Settings > Privacy & Security.
     4) Scroll down to find the block notice for LocalSynapse
        and click "Open Anyway".
     5) Confirm with your password or Touch ID.

   NOTE: macOS 15 Sequoia removed the older "Control + click >
   Open" bypass that worked on earlier versions. If you are on
   macOS 15 and see only "Done" and "Move to Trash" buttons,
   use Option A or B above.

   You only need to do this once.
   Subsequent launches will work without any extra steps.

3. MORE INFO
   https://localsynapse.com
   https://github.com/LocalSynapse/LocalSynapse
