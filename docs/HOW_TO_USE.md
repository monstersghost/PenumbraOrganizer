# How to Use Penumbra Organizer

_v0.3.3-beta: an unofficial, third-party Windows utility. Not affiliated with Square Enix,
XIVLauncher, Dalamud, or the Penumbra maintainers._

## What this app does

Penumbra Organizer reorganizes your Penumbra virtual mod folders without touching your actual
mod files on disk. It reads Penumbra's configuration, lets you plan folder changes, and writes
those changes back only after you review and approve them, behind a verified backup.

It does not modify FFXIV game files, move physical mod directories, or edit `.pmp` packages.

## Before you start

* Windows 10 or 11, 64-bit.
* XIVLauncher/Dalamud with Penumbra installed.
* Close FFXIV before applying any change (the app will remind you, but plan for it).
* No installation needed. The app is a single self-contained `.exe`.

## 1. First launch

1. Open `PenumbraOrganizer.exe`.
2. The app tries to detect your Penumbra installation automatically. Confirm the Penumbra state
   directory and mod library root it shows you. If detection is wrong or your setup is unusual,
   you can override both paths manually.
3. Click **Scan My Mods**. This step only reads your current setup; it changes nothing.

## 2. Choose a sort strategy

Open the **Sort Method** tab and pick one:

* **Start manually**: build your folder structure by hand.
* **By creator**: one folder per mod author.
* **By mod type**: grouped by what kind of mod it is (gear, hair, animations, and so on).
* **Type then creator** / **Creator then type**: nested folders combining both.
* **Preserve and clean**: keep your current folder assignments as they are. Use this if you
  only want to run Folder Cleanup (see below) without reorganizing anything else.
* **Custom**: start from a template you've adjusted yourself.

Picking a strategy builds a full proposed plan; nothing is written to Penumbra yet.

If you'd rather plan folders in a spreadsheet instead of the app's own tabs, see the Workbook
workflow near the end of this guide.

## 3. Review and protect mods

Open **Current Mods** to search or filter your library. If there are mods or folders you never
want touched, select them and click **Protect Selected** (for mods in the grid) or **Protect
Folder** (for a folder in the tree). Protected items are skipped by every strategy and every
Apply.

## 4. Adjust proposed changes

Open **Proposed Changes** to fine-tune individual folder assignments. **Undo** and **Redo** work
here if you want to back out an adjustment.

## 5. Clean up empty folders left behind by Penumbra

This is a separate, optional step from reorganizing your mods, so it lives in its own tab.

**Why folders show up here:** Penumbra keeps its own list of every folder it has ever created, in
a file called `organization.json`. When you sort one way and later switch to a different scheme,
or roll back a change, the old folders can stay in that list even though nothing is in them
anymore. Penumbra recreates them every time it loads, even after a full game restart, and only an
explicit delete inside Penumbra itself removes one. Penumbra Organizer previously had no way to
see or fix this.

**To clean them up:**

1. Open the **Folder Cleanup** tab (between Proposed Changes and Review Changes).
2. The app lists any empty folders it found in `organization.json`, split into two groups:
   folders with no customization, and folders that still have a color, sort mode, or separator
   set on them.
3. Check the boxes next to the folders you want removed. This release limits Folder Cleanup to 3
   folders per Apply by default: it's a brand new feature, and this cap keeps each Apply small
   while it's still being validated on real installs. If you have more than 3 orphaned folders,
   either repeat the process in batches of 3, or use **Advanced Cleanup** (see below) to remove
   the limit for your own testing.

**If you have a lot of orphaned folders (hundreds or more):** click **Enable Advanced Cleanup (at
your own risk)** at the top of the Folder Cleanup tab. You'll get a confirmation dialog explaining
the risk, with a checkbox you have to tick before it lets you continue. Once enabled, **Select
All** and Apply can prune every orphaned folder it finds in one go instead of 3 at a time. This
bypasses a safety limit on a feature that's still new, so only use it if you understand the risk
and, ideally, have a manual backup of your Penumbra config folder first (see the manual backup
section below). It resets automatically every time you restart the app.
4. Continue to Review Changes and Apply as normal. Cleanup runs through the exact same dry run,
   backup, and Apply steps as any other change.
5. After Apply, the app asks you to check Penumbra and confirm whether the folder actually
   disappeared, right away or after a reload or restart. Answer honestly; this helps confirm the
   fix is working on your setup.

If you don't have any orphaned folders, or you'd rather not touch this yet, just leave everything
unchecked and move on. It's entirely optional.

## 6. Review changes

Open **Review Changes**. This is the app's validation pass; it flags anything it thinks needs your
attention before you're allowed to proceed. Resolve any flagged items before continuing.

## 7. Close FFXIV

The app cannot safely write to Penumbra's configuration files while the game has them open. Close
FFXIV completely before the next step.

## 8. Backup and Apply

Click **Create Backup**, then **Backup and Apply**. The app builds a verified backup of your
entire Penumbra configuration directory (not just the files it's about to change) before writing
anything. Only after that backup succeeds does it apply your reviewed changes.

## 9. 📦 Back up your Penumbra configuration manually (extra safety)

The app already creates a verified backup before every Apply. If you'd also like your own manual
copy set aside somewhere outside the app, here's how:

1. Press **Windows + R** on your keyboard.
2. A small window called **Run** will appear.
3. Click inside the text box.
4. Copy and paste the following:

   ```text
   %AppData%\XIVLauncher\pluginConfigs
   ```

5. Press **Enter**.
6. File Explorer will open to the plugin configuration folder.
7. Find the folder named **Penumbra**.
8. **Copy** the entire **Penumbra** folder.
9. Paste it somewhere safe (for example, another drive, a USB stick, or a cloud storage folder).

> 💡 If anything ever goes wrong, you can restore your settings by replacing the `Penumbra` folder
> with your backup.

## 10. If something goes wrong: restore a backup

Open the **Backups** tab.

* **Restore Backup** rolls back an entire previous operation.
* **Restore Selected File Only** rolls back just one file from that backup, such as
  `organization.json`, without touching anything else. Note: restoring `organization.json` on its
  own can bring back the orphaned-folder problem that Folder Cleanup fixes, since it puts the old,
  unpruned version back. Use this only if you specifically need that file reverted.

## Optional: the Workbook workflow

If you'd rather review your planned changes outside the app, use **Export Workbook** to save them
to an Excel file. Edit it in any spreadsheet tool, then **Import Workbook** to bring your edits
back in. The import is checked against your current Penumbra inventory first; if a mod moved or a
row no longer matches since you exported, only that row is skipped (with a reason), and everything
else still imports.

You don't need Microsoft Excel for this. **LibreOffice Calc** is a free, trusted alternative that
opens and saves the same file format without any licensing cost.

## Safety summary

* Scanning never writes anything.
* Nothing is written to Penumbra until you click Backup and Apply.
* A verified backup is created before every Apply.
* Protected mods and folders are never changed.
* Your actual mod files on disk are never touched.

## Where your data lives

The app stores its own sessions, settings, and logs under `%LocalAppData%\PenumbraOrganizer\`.
Manual Penumbra config backups and diagnostic packages default to a folder there too, but you're
asked to choose a save location every time you create one.

## Reporting a problem

If something doesn't work, please include:

* the Penumbra Organizer version (shown on the Home and Settings tabs)
* your Windows version and Penumbra version
* what you clicked, in order
* the exact error message shown
* sanitized logs, if asked for them

Please don't upload mod files, private paid mods, your entire Penumbra configuration, unredacted
personal file paths, or any credentials.

Support and bug reports: https://discord.gg/MhQzVJ65c
