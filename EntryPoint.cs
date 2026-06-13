using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Rage;
using RAGENativeUI;
using RAGENativeUI.Elements;

[assembly: Rage.Attributes.Plugin(
    "San Andreas Booking System",
    Description = "Booking desk processing system for LSPDFR.",
    Author = "Dh"
)]

namespace SanAndreasBookingSystem
{
    public static class EntryPoint
    {
        // -------------------------------------------------------------------
        // ADAPTER
        // Swap FakePolicingRedefinedAdapter for a real implementation later.
        // -------------------------------------------------------------------
        private static readonly IPolicingRedefinedAdapter adapter = new FakePolicingRedefinedAdapter();

        // -------------------------------------------------------------------
        // MENU SYSTEM
        // -------------------------------------------------------------------
        private static MenuPool menuPool;

        private static UIMenu cellMenu;
        private static UIMenu bookingMenu;
        private static UIMenu suspectInfoMenu;
        private static UIMenu searchResultsMenu;
        private static UIMenu evidenceMenu;
        private static UIMenu fingerprintMenu;
        private static UIMenu reportMenu;
        private static UIMenu jailMenu;

        private static UIMenu activeMenu;

        private static readonly Keys OpenMenuKey = Keys.F7;
        private static bool backWasDown = false;

        private static readonly Random random = new Random();

        // -------------------------------------------------------------------
        // CELL / SUSPECT DATA
        // -------------------------------------------------------------------
        private static List<BookingSuspect> cellSuspects;
        private static List<UIMenuItem> cellItems;
        private static BookingSuspect selectedSuspect;

        // -------------------------------------------------------------------
        // SUSPECT INFO MENU ITEMS (updated when a suspect is selected)
        // -------------------------------------------------------------------
        private static UIMenuItem suspectNameItem;
        private static UIMenuItem suspectDobItem;
        private static UIMenuItem suspectGenderItem;
        private static UIMenuItem suspectIdItem;
        private static UIMenuItem suspectLicenseItem;
        private static UIMenuItem suspectWantedItem;
        private static UIMenuItem suspectCellItem;

        // -------------------------------------------------------------------
        // DYNAMIC SEARCH RESULT ITEMS
        // -------------------------------------------------------------------
        private static List<UIMenuItem> searchResultItems;

        // -------------------------------------------------------------------
        // STATUS DISPLAY ITEMS
        // -------------------------------------------------------------------
        private static UIMenuItem evidenceStatusItem;
        private static UIMenuItem fingerprintStatusItem;
        private static UIMenuItem reportStatusItem;
        private static UIMenuItem jailStatusItem;

        // ===================================================================
        // ENTRY POINT
        // ===================================================================

        public static void Main()
        {
            Game.DisplayNotification("~g~San Andreas Booking System loaded!");
            Game.LogTrivial("San Andreas Booking System loaded.");

            // Load fake data first so the menus have something to display
            // even before ped detection finds anyone in a cell.
            LoadFakeCellData();
            CreateMenus();

            Game.DisplayHelp("Press ~b~F7~w~ to open the Booking Cell menu.");

            while (true)
            {
                GameFiber.Yield();

                HandleBackspaceNavigation();
                menuPool.ProcessMenus();

                if (Game.IsKeyDown(OpenMenuKey) && !UIMenu.IsAnyMenuVisible)
                {
                    // Rescan cells for peds before showing the list.
                    // This keeps the list up-to-date each time the player opens it.
                    RefreshCellsFromWorld();
                    OpenCellMenu();
                }
            }
        }

        public static void Finally()
        {
            Game.LogTrivial("San Andreas Booking System unloaded.");
        }

        // ===================================================================
        // DATA LOADING
        // ===================================================================

        /// <summary>
        /// Loads hard-coded fake suspects into each cell.
        /// This is used as a fallback / development mode.
        /// When ped detection is working these records will be overwritten
        /// by RefreshCellsFromWorld().
        /// </summary>
        private static void LoadFakeCellData()
        {
            cellSuspects = new List<BookingSuspect>
            {
                new BookingSuspect
                {
                    CellNumber    = 1,
                    Name          = "John Doe",
                    DateOfBirth   = "01/01/1995",
                    Gender        = "Male",
                    IdStatus      = "Valid",
                    LicenseStatus = "Suspended",
                    WantedStatus  = "No Active Warrants",
                    SearchItems   = new List<string>
                    {
                        "Bag of Methamphetamine",
                        "Lockpick",
                        "$420 Cash",
                        "Phone",
                        "Wallet"
                    }
                },

                new BookingSuspect
                {
                    CellNumber = 2,
                    Name       = ""           // Empty cell
                },

                new BookingSuspect
                {
                    CellNumber    = 3,
                    Name          = "Marcus Johnson",
                    DateOfBirth   = "04/18/1994",
                    Gender        = "Male",
                    IdStatus      = "Valid",
                    LicenseStatus = "Valid",
                    WantedStatus  = "Possible Probation Match",
                    SearchItems   = new List<string>
                    {
                        "Small Bag of Cocaine",
                        "$190 Cash",
                        "Cell Phone"
                    }
                },

                new BookingSuspect
                {
                    CellNumber = 4,
                    Name       = ""           // Empty cell
                }
            };
        }

        /// <summary>
        /// Uses CellManager to scan the world for peds in/near each cell
        /// coordinate and builds a fresh cellSuspects list from that.
        ///
        /// Any booking progress already recorded for a suspect (evidence,
        /// fingerprints, etc.) is preserved if the same ped is still in the
        /// same cell after the rescan.
        ///
        /// Falls back to fake data if no peds are found near any cell
        /// (useful when testing outside of an active callout).
        /// </summary>
        private static void RefreshCellsFromWorld()
        {
            List<BookingSuspect> scanned = CellManager.ScanCells(adapter);

            // Count how many peds were actually detected in the world.
            int detectedPeds = 0;
            foreach (BookingSuspect s in scanned)
            {
                if (!s.IsEmpty) detectedPeds++;
            }

            if (detectedPeds == 0)
            {
                // No peds found near any cell — keep the existing data
                // (either fake data or previously loaded suspects).
                Game.LogTrivial("CellManager: No peds detected near cells. Keeping existing data.");
                return;
            }

            // Merge: carry over booking progress for suspects still in the
            // same cell so a rescan doesn't wipe partial booking work.
            foreach (BookingSuspect fresh in scanned)
            {
                if (fresh.IsEmpty) continue;

                // Find the matching existing record by cell number.
                BookingSuspect existing = cellSuspects.Find(s => s.CellNumber == fresh.CellNumber);

                if (existing != null && !existing.IsEmpty &&
                    existing.AssignedPed == fresh.AssignedPed)
                {
                    // Same ped is still in this cell — copy their booking progress over.
                    fresh.CaseNumber = existing.CaseNumber;
                    fresh.EvidencePackageSubmitted = existing.EvidencePackageSubmitted;
                    fresh.BodycamSubmitted = existing.BodycamSubmitted;
                    fresh.FingerprintPending = existing.FingerprintPending;
                    fresh.FingerprintCompleted = existing.FingerprintCompleted;
                    fresh.ReportGenerated = existing.ReportGenerated;
                    fresh.ChargesConfirmed = existing.ChargesConfirmed;
                    fresh.CaseFinalized = existing.CaseFinalized;
                }
            }

            cellSuspects = scanned;
            Game.LogTrivial("CellManager: Refreshed cell data. " + detectedPeds + " ped(s) detected.");
        }

        // ===================================================================
        // MENU CREATION
        // ===================================================================

        private static void CreateMenus()
        {
            menuPool = new MenuPool();

            CreateCellMenu();
            CreateBookingMenu();
            CreateSuspectInfoMenu();
            CreateSearchResultsMenu();
            CreateEvidenceMenu();
            CreateFingerprintMenu();
            CreateReportMenu();
            CreateJailMenu();

            menuPool.Add(cellMenu);
            menuPool.Add(bookingMenu);
            menuPool.Add(suspectInfoMenu);
            menuPool.Add(searchResultsMenu);
            menuPool.Add(evidenceMenu);
            menuPool.Add(fingerprintMenu);
            menuPool.Add(reportMenu);
            menuPool.Add(jailMenu);
        }

        /// <summary>
        /// Cell list — shows one item per cell, coloured green (occupied) or
        /// red (empty). Clicking an occupied cell opens the booking menu for
        /// that suspect.
        /// </summary>
        private static void CreateCellMenu()
        {
            cellMenu = new UIMenu("Booking Cells", "SELECT SUSPECT");
            cellItems = new List<UIMenuItem>();

            // Create one item per cell. Text is filled in by UpdateCellMenuItems().
            for (int i = 0; i < 4; i++)
            {
                UIMenuItem item = new UIMenuItem("", "Select a cell to begin booking.");
                cellItems.Add(item);
                cellMenu.AddItem(item);
            }

            cellMenu.OnItemSelect += (sender, item, index) =>
            {
                // Guard: make sure index is within bounds in case cell count differs.
                if (index < 0 || index >= cellSuspects.Count) return;

                BookingSuspect suspect = cellSuspects[index];

                if (suspect.IsEmpty)
                {
                    Game.DisplayNotification("~r~Cell " + suspect.CellNumber + " is empty.");
                    return;
                }

                selectedSuspect = suspect;
                UpdateAllMenusForSelectedSuspect();
                OpenBookingMenu();
            };
        }

        /// <summary>
        /// Main booking desk — lists every available action for the selected suspect.
        /// </summary>
        private static void CreateBookingMenu()
        {
            bookingMenu = new UIMenu("Booking Desk", "SAN ANDREAS BOOKING SYSTEM");

            bookingMenu.AddItem(new UIMenuItem("View Suspect Info",
                "View suspect identity, ID status, warrants, and booking cell."));
            bookingMenu.AddItem(new UIMenuItem("View Search Results",
                "View items found during the suspect search."));
            bookingMenu.AddItem(new UIMenuItem("Submit Evidence",
                "Submit evidence package and bodycam footage."));
            bookingMenu.AddItem(new UIMenuItem("Add / Confirm Charges",
                "Close this menu and use PDComp to add charges."));
            bookingMenu.AddItem(new UIMenuItem("Fingerprint Scan",
                "Run suspect fingerprints through the database."));
            bookingMenu.AddItem(new UIMenuItem("Write Booking Report",
                "Generate a booking report for this suspect."));
            bookingMenu.AddItem(new UIMenuItem("Send to Jail",
                "Finalize booking once all required steps are complete."));

            bookingMenu.OnItemSelect += (sender, item, index) =>
            {
                if (selectedSuspect == null)
                {
                    Game.DisplayNotification("~r~No suspect selected.");
                    OpenCellMenu();
                    return;
                }

                switch (index)
                {
                    case 0: OpenSubMenu(suspectInfoMenu); break;
                    case 1: OpenSubMenu(searchResultsMenu); break;
                    case 2: OpenSubMenu(evidenceMenu); break;
                    case 3: OpenPdCompPlaceholder(); break;
                    case 4: OpenSubMenu(fingerprintMenu); break;
                    case 5: OpenSubMenu(reportMenu); break;
                    case 6: OpenSubMenu(jailMenu); break;
                }
            };
        }

        /// <summary>
        /// Suspect identity info — read-only display items, no actions.
        /// </summary>
        private static void CreateSuspectInfoMenu()
        {
            suspectInfoMenu = new UIMenu("Suspect Info", "IDENTITY RECORD");

            suspectNameItem = new UIMenuItem("Name: —", "");
            suspectDobItem = new UIMenuItem("DOB: —", "");
            suspectGenderItem = new UIMenuItem("Gender: —", "");
            suspectIdItem = new UIMenuItem("ID Status: —", "");
            suspectLicenseItem = new UIMenuItem("License Status: —", "");
            suspectWantedItem = new UIMenuItem("Wanted Status: —", "");
            suspectCellItem = new UIMenuItem("Cell: —", "");

            suspectInfoMenu.AddItem(suspectNameItem);
            suspectInfoMenu.AddItem(suspectDobItem);
            suspectInfoMenu.AddItem(suspectGenderItem);
            suspectInfoMenu.AddItem(suspectIdItem);
            suspectInfoMenu.AddItem(suspectLicenseItem);
            suspectInfoMenu.AddItem(suspectWantedItem);
            suspectInfoMenu.AddItem(suspectCellItem);

            // These items are display-only — clicking does nothing.
            suspectInfoMenu.OnItemSelect += (sender, item, index) => { };
        }

        /// <summary>
        /// Search results — dynamically rebuilt each time a suspect is selected
        /// so the item count always matches the actual number of search items.
        /// </summary>
        private static void CreateSearchResultsMenu()
        {
            searchResultsMenu = new UIMenu("Search Results", "ITEMS FOUND ON SUSPECT");
            searchResultItems = new List<UIMenuItem>();

            // We'll add/remove items dynamically in UpdateSearchResultsMenu().
            searchResultsMenu.OnItemSelect += (sender, item, index) => { };
        }

        /// <summary>
        /// Evidence submission — two submit actions plus a combined status display.
        /// </summary>
        private static void CreateEvidenceMenu()
        {
            evidenceMenu = new UIMenu("Evidence", "CASE EVIDENCE");

            evidenceMenu.AddItem(new UIMenuItem("Submit Evidence Package",
                "Document, seal, and transfer physical evidence to the evidence locker."));
            evidenceMenu.AddItem(new UIMenuItem("Submit Bodycam Footage",
                "Upload and attach bodycam footage to the booking case."));

            evidenceStatusItem = new UIMenuItem("Evidence Status: ~r~Not Submitted~w~",
                "Evidence is only marked Submitted when BOTH package and bodycam are complete.");
            evidenceMenu.AddItem(evidenceStatusItem);

            evidenceMenu.OnItemSelect += (sender, item, index) =>
            {
                if (selectedSuspect == null) return;

                if (index == 0) SubmitEvidencePackage();
                else if (index == 1) SubmitBodycamFootage();
                // index 2 is the status display item — no action
            };
        }

        /// <summary>
        /// Fingerprint scan — run scan action and a live status display.
        /// </summary>
        private static void CreateFingerprintMenu()
        {
            fingerprintMenu = new UIMenu("Fingerprints", "DATABASE SCAN");

            fingerprintMenu.AddItem(new UIMenuItem("Run Fingerprint Scan",
                "Scan suspect fingerprints and cross-reference with database records."));

            fingerprintStatusItem = new UIMenuItem("Fingerprint Status: ~r~Not Completed~w~",
                "Current fingerprint scan status.");
            fingerprintMenu.AddItem(fingerprintStatusItem);

            fingerprintMenu.OnItemSelect += (sender, item, index) =>
            {
                if (selectedSuspect == null) return;
                if (index == 0) RunFingerprintScan();
            };
        }

        /// <summary>
        /// Booking report — generate and view status.
        /// </summary>
        private static void CreateReportMenu()
        {
            reportMenu = new UIMenu("Booking Report", "REPORT WRITING");

            reportMenu.AddItem(new UIMenuItem("Generate Report",
                "Generate a booking report for this suspect and attach it to the case file."));

            reportStatusItem = new UIMenuItem("Report Status: ~r~Not Generated~w~",
                "Current report status.");
            reportMenu.AddItem(reportStatusItem);

            reportMenu.OnItemSelect += (sender, item, index) =>
            {
                if (selectedSuspect == null) return;
                if (index == 0) GenerateReport();
            };
        }

        /// <summary>
        /// Send to jail — finalise the booking once all steps are complete.
        /// </summary>
        private static void CreateJailMenu()
        {
            jailMenu = new UIMenu("Send to Jail", "FINALIZE BOOKING");

            jailMenu.AddItem(new UIMenuItem("Transfer Suspect to Jail",
                "Finalize booking and transfer suspect to county jail. All steps must be complete."));

            jailStatusItem = new UIMenuItem("Case Status: ~r~Not Finalized~w~",
                "Current booking case status. All steps must be completed before finalizing.");
            jailMenu.AddItem(jailStatusItem);

            jailMenu.OnItemSelect += (sender, item, index) =>
            {
                if (selectedSuspect == null) return;
                if (index == 0) FinalizeBooking();
            };
        }

        // ===================================================================
        // NAVIGATION
        // ===================================================================

        private static void OpenCellMenu()
        {
            CloseAllMenus();
            UpdateCellMenuItems();
            cellMenu.Visible = true;
            activeMenu = cellMenu;
        }

        private static void OpenBookingMenu()
        {
            CloseAllMenus();

            // Show the selected suspect's name in the subtitle bar.
            bookingMenu.Subtitle.Caption = selectedSuspect != null
                ? selectedSuspect.Name.ToUpper()
                : "NO SUSPECT";

            bookingMenu.Visible = true;
            activeMenu = bookingMenu;

            Game.DisplaySubtitle("Press ~b~Backspace~w~ to return to the cell list.");
        }

        private static void OpenSubMenu(UIMenu menu)
        {
            CloseAllMenus();
            menu.Visible = true;
            activeMenu = menu;

            Game.DisplaySubtitle("Press ~b~Backspace~w~ to return to the Booking Desk menu.");
        }

        private static void CloseAllMenus()
        {
            cellMenu.Visible = false;
            bookingMenu.Visible = false;
            suspectInfoMenu.Visible = false;
            searchResultsMenu.Visible = false;
            evidenceMenu.Visible = false;
            fingerprintMenu.Visible = false;
            reportMenu.Visible = false;
            jailMenu.Visible = false;
        }

        /// <summary>
        /// Backspace navigation:
        ///   Sub-menu  → Booking menu
        ///   Booking menu → Cell list
        ///   Cell list → Close (no menu visible)
        ///
        /// Uses edge-detect (pressed this frame, not last frame) to avoid repeat-fire.
        /// </summary>
        private static void HandleBackspaceNavigation()
        {
            bool backDown = Game.IsKeyDown(Keys.Back);

            if (backDown && !backWasDown)
            {
                if (activeMenu == null)
                {
                    backWasDown = backDown;
                    return;
                }

                if (activeMenu == cellMenu)
                {
                    // Close everything
                    cellMenu.Visible = false;
                    activeMenu = null;
                }
                else if (activeMenu == bookingMenu)
                {
                    // Go back to cell list
                    OpenCellMenu();
                }
                else
                {
                    // Any sub-menu goes back to booking menu
                    OpenBookingMenu();
                }
            }

            backWasDown = backDown;
        }

        // ===================================================================
        // MENU DATA UPDATES
        // ===================================================================

        /// <summary>
        /// Refreshes the cell list item labels to reflect current cell data.
        /// Green = suspect present, Red = empty.
        /// </summary>
        private static void UpdateCellMenuItems()
        {
            // Make sure we have exactly as many UIMenuItems as cell records.
            // If cell count changed (unlikely but safe), rebuild.
            while (cellItems.Count < cellSuspects.Count)
            {
                UIMenuItem newItem = new UIMenuItem("", "");
                cellItems.Add(newItem);
                cellMenu.AddItem(newItem);
            }

            for (int i = 0; i < cellSuspects.Count; i++)
            {
                BookingSuspect s = cellSuspects[i];

                if (s.IsEmpty)
                {
                    cellItems[i].Text = "Cell " + s.CellNumber + ": ~r~Empty~w~";
                    cellItems[i].Description = "No suspect is currently assigned to this cell.";
                }
                else
                {
                    // Show a yellow dot if booking is in progress, green if finalized.
                    string statusTag = s.CaseFinalized ? "~g~" : "~y~";
                    cellItems[i].Text = "Cell " + s.CellNumber + ": " + statusTag + s.Name + "~w~";
                    cellItems[i].Description = "Open booking file for " + s.Name + ".";
                }
            }
        }

        /// <summary>
        /// Called when a suspect is selected. Updates all sub-menu display
        /// items to reflect that suspect's data.
        /// </summary>
        private static void UpdateAllMenusForSelectedSuspect()
        {
            if (selectedSuspect == null) return;

            // Suspect info items
            suspectNameItem.Text = "Name: " + selectedSuspect.Name;
            suspectDobItem.Text = "DOB: " + selectedSuspect.DateOfBirth;
            suspectGenderItem.Text = "Gender: " + selectedSuspect.Gender;
            suspectIdItem.Text = "ID Status: " + selectedSuspect.IdStatus;
            suspectLicenseItem.Text = "License Status: " + selectedSuspect.LicenseStatus;
            suspectWantedItem.Text = "Wanted Status: " + selectedSuspect.WantedStatus;
            suspectCellItem.Text = "Cell: " + selectedSuspect.CellNumber;

            UpdateSearchResultsMenu();
            UpdateStatusItems();
        }

        /// <summary>
        /// Rebuilds the search results menu items dynamically so the count
        /// always matches the actual number of items found on this suspect.
        /// Empty placeholder rows are removed.
        /// </summary>
        private static void UpdateSearchResultsMenu()
        {
            // Clear old items from the menu and our tracking list.
            searchResultsMenu.Clear();
            searchResultItems.Clear();

            List<string> items = selectedSuspect.SearchItems;

            if (items == null || items.Count == 0)
            {
                UIMenuItem empty = new UIMenuItem("~y~No items found~w~",
                    "No search results available for this suspect.");
                searchResultItems.Add(empty);
                searchResultsMenu.AddItem(empty);
                return;
            }

            foreach (string itemText in items)
            {
                UIMenuItem menuItem = new UIMenuItem(itemText,
                    "Item found during suspect search. Source: Policing Redefined (placeholder).");
                searchResultItems.Add(menuItem);
                searchResultsMenu.AddItem(menuItem);
            }

            searchResultsMenu.RefreshIndex();
        }

        /// <summary>
        /// Updates all four status display items based on the selected
        /// suspect's current booking progress flags.
        /// </summary>
        private static void UpdateStatusItems()
        {
            if (selectedSuspect == null) return;

            // Evidence status — both package AND bodycam must be done
            if (selectedSuspect.EvidenceComplete)
                evidenceStatusItem.Text = "Evidence Status: ~g~Submitted~w~";
            else if (selectedSuspect.EvidencePackageSubmitted || selectedSuspect.BodycamSubmitted)
                evidenceStatusItem.Text = "Evidence Status: ~y~Partial~w~";
            else
                evidenceStatusItem.Text = "Evidence Status: ~r~Not Submitted~w~";

            // Fingerprint status — pending or completed
            if (selectedSuspect.FingerprintPending)
                fingerprintStatusItem.Text = "Fingerprint Status: ~y~Pending~w~";
            else if (selectedSuspect.FingerprintCompleted)
                fingerprintStatusItem.Text = "Fingerprint Status: ~g~Completed~w~";
            else
                fingerprintStatusItem.Text = "Fingerprint Status: ~r~Not Completed~w~";

            // Report status
            reportStatusItem.Text = selectedSuspect.ReportGenerated
                ? "Report Status: ~g~Generated~w~"
                : "Report Status: ~r~Not Generated~w~";

            // Case / jail status
            jailStatusItem.Text = selectedSuspect.CaseFinalized
                ? "Case Status: ~g~Finalized~w~"
                : "Case Status: ~r~Not Finalized~w~";
        }

        // ===================================================================
        // BOOKING ACTIONS
        // ===================================================================

        private static void SubmitEvidencePackage()
        {
            if (selectedSuspect.EvidencePackageSubmitted)
            {
                Game.DisplayNotification("~y~Evidence package already submitted for this case.");
                return;
            }

            EnsureCaseNumber();
            selectedSuspect.EvidencePackageSubmitted = true;
            UpdateStatusItems();

            string time = DateTime.Now.ToString("MM/dd/yyyy hh:mm tt");
            Game.DisplayNotification(
                "~b~Evidence Control~w~: Evidence package has been ~g~documented, sealed, and logged~w~ " +
                "into secure storage.~n~" +
                "Case: ~y~" + selectedSuspect.CaseNumber + "~w~ | " + time
            );

            Game.LogTrivial("[SABS] Evidence package submitted | Case " + selectedSuspect.CaseNumber);
        }

        private static void SubmitBodycamFootage()
        {
            if (selectedSuspect.BodycamSubmitted)
            {
                Game.DisplayNotification("~y~Bodycam footage already submitted for this case.");
                return;
            }

            EnsureCaseNumber();
            selectedSuspect.BodycamSubmitted = true;
            UpdateStatusItems();

            string time = DateTime.Now.ToString("MM/dd/yyyy hh:mm tt");
            Game.DisplayNotification(
                "~b~Digital Evidence~w~: Bodycam footage has been ~g~uploaded, timestamped, and attached~w~ " +
                "to the booking file.~n~" +
                "Case: ~y~" + selectedSuspect.CaseNumber + "~w~ | " + time
            );

            Game.LogTrivial("[SABS] Bodycam submitted | Case " + selectedSuspect.CaseNumber);
        }

        /// <summary>
        /// Closes the booking menu and shows instructions to add charges via PDComp.
        /// Direct PDComp integration would go here once its API (if any) is known.
        /// </summary>
        private static void OpenPdCompPlaceholder()
        {
            // Close all menus so PDComp can open unobstructed
            CloseAllMenus();
            activeMenu = null;

            Game.DisplayNotification(
                "~b~Charges~w~: Booking menu closed.~n~" +
                "Open ~y~PDComp~w~ and add charges for ~g~" + selectedSuspect.Name + "~w~.~n~" +
                "Return here when finished and press ~b~F7~w~ to reopen the booking desk."
            );
        }

        /// <summary>
        /// Starts the fingerprint scan flow:
        ///   1. Status → Pending, notification sent.
        ///   2. After 3 seconds (on a background fiber) → Status → Completed.
        /// </summary>
        private static void RunFingerprintScan()
        {
            if (selectedSuspect.FingerprintPending)
            {
                Game.DisplayNotification("~y~Fingerprint scan is already pending for " + selectedSuspect.Name + ".");
                return;
            }

            if (selectedSuspect.FingerprintCompleted)
            {
                Game.DisplayNotification("~g~Fingerprint scan already completed for " + selectedSuspect.Name + ".");
                return;
            }

            selectedSuspect.FingerprintPending = true;
            UpdateStatusItems();

            Game.DisplayNotification(
                "~b~Fingerprint Scanner~w~: Scan submitted. Database match is ~y~pending~w~..."
            );

            // Capture a local reference so the fiber still works if the
            // player selects a different suspect before the timer fires.
            BookingSuspect scanTarget = selectedSuspect;

            GameFiber.StartNew(delegate
            {
                GameFiber.Sleep(3000);

                scanTarget.FingerprintPending = false;
                scanTarget.FingerprintCompleted = true;

                // Only update the UI if this suspect is still selected.
                if (selectedSuspect == scanTarget)
                    UpdateStatusItems();

                Game.DisplayNotification(
                    "~b~Fingerprint Scanner~w~: Match ~g~confirmed~w~ for ~g~" +
                    scanTarget.Name + "~w~. Identity record has been attached to the booking file."
                );

                Game.LogTrivial("[SABS] Fingerprint scan completed for " + scanTarget.Name);
            });
        }

        private static void GenerateReport()
        {
            if (selectedSuspect.ReportGenerated)
            {
                Game.DisplayNotification("~y~Report already generated | Case " + selectedSuspect.CaseNumber);
                return;
            }

            EnsureCaseNumber();
            selectedSuspect.ReportGenerated = true;
            UpdateStatusItems();

            string time = DateTime.Now.ToString("MM/dd/yyyy hh:mm tt");
            Game.DisplayNotification(
                "~b~Booking Report~w~: Report generated and attached to case ~y~" +
                selectedSuspect.CaseNumber + "~w~.~n~Generated: " + time
            );

            Game.LogTrivial("[SABS] Report generated | Case " + selectedSuspect.CaseNumber);
        }

        /// <summary>
        /// Finalises the booking. Checks that ALL required steps are complete
        /// and gives specific feedback if something is missing.
        /// </summary>
        private static void FinalizeBooking()
        {
            if (selectedSuspect.CaseFinalized)
            {
                Game.DisplayNotification("~g~Case " + selectedSuspect.CaseNumber + " is already finalized.");
                return;
            }

            string missing = GetMissingSteps();

            if (!string.IsNullOrEmpty(missing))
            {
                Game.DisplayNotification(
                    "~r~Booking cannot be finalized yet.~w~~n~" +
                    "Still required: " + missing
                );
                return;
            }

            selectedSuspect.CaseFinalized = true;
            UpdateStatusItems();

            // Update the cell list colour for this suspect immediately.
            UpdateCellMenuItems();

            Game.DisplayNotification(
                "~b~Booking Desk~w~: Case ~y~" + selectedSuspect.CaseNumber +
                "~w~ has been ~g~finalized~w~. " + selectedSuspect.Name +
                " is ready for jail transfer."
            );

            Game.LogTrivial("[SABS] Booking finalized | Case " + selectedSuspect.CaseNumber);
        }

        /// <summary>
        /// Returns a comma-separated list of incomplete steps, or an empty
        /// string if everything is done.
        /// </summary>
        private static string GetMissingSteps()
        {
            List<string> missing = new List<string>();

            if (!selectedSuspect.EvidencePackageSubmitted)
                missing.Add("~r~Evidence Package~w~");

            if (!selectedSuspect.BodycamSubmitted)
                missing.Add("~r~Bodycam Footage~w~");

            if (!selectedSuspect.FingerprintCompleted)
                missing.Add("~r~Fingerprint Scan~w~");

            if (!selectedSuspect.ReportGenerated)
                missing.Add("~r~Booking Report~w~");

            return string.Join(", ", missing.ToArray());
        }

        // ===================================================================
        // HELPERS
        // ===================================================================

        /// <summary>
        /// Generates a unique case number for the selected suspect if they
        /// don't already have one. Format: SA-YYYY-NNNNN
        /// </summary>
        private static void EnsureCaseNumber()
        {
            if (!string.IsNullOrEmpty(selectedSuspect.CaseNumber)) return;

            selectedSuspect.CaseNumber =
                "SA-" + DateTime.Now.ToString("yyyy") + "-" + random.Next(10000, 99999);
        }
    }
}