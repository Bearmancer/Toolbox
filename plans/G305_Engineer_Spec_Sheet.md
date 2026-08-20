# 🔧 Engineer Work Order — Logitech G304/G305 Mouse
### Right Click Microswitch Replacement
**Prepared for:** Phone/Mobile Repair Technician (first mouse repair)
**Device:** Logitech G304/G305 Lightspeed Wireless Mouse
**Job Type:** Through-hole microswitch desolder + resolder
**Estimated Time:** 15–20 minutes

---

## 📦 Parts Handed to You by Customer

| Item | Qty | Notes |
| :--- | :--- | :--- |
| Logitech G305 mouse | 1 | Customer's device |
| Replacement 3-pin microswitch (e.g. Kailh GM 8.0) | 2 pcs | Replace BOTH left + right click |
| Replacement PTFE mouse feet/skates | 1 set | Needed for reassembly |

---

## 🔩 Tools You Already Have (Same as Phone Repair)

- Temperature-controlled soldering iron ← **same as you use daily**
- Desoldering pump or solder wick ← **same as you use daily**
- Flux paste or liquid flux ← **same as you use daily**
- Phillips #00 screwdriver (the small one for phone screens)
- Plastic pry tool / spudger (same as phone back panel opening)
- Isopropyl alcohol (IPA) + cotton swab
- Tweezers

---

## ⚡ Key Differences vs Phone Repair (Read This First)

| What You Know | How Mouse Is Different |
| :--- | :--- |
| Phone uses tiny SMD components | Mouse switches are **through-hole** — 3 physical pins go through holes in the PCB. Easier to desolder than SMD. |
| Phone ribbon cables are fragile | Mouse has **one ZIF ribbon cable** + one battery wire. Same fragility — same respect needed. |
| Phone screws are under the screen | Mouse screws are under the **rubber feet on the bottom** — peel them first. |
| Phone PCB is one main board | Mouse has **two boards** — a small switch daughterboard (the one you'll work on) + a main board below. You only need to remove and work on the small one. |
| Phone chips need hot air | These switches only need a **standard iron tip** — no hot air station required. |

---

## 📋 Step-by-Step Procedure

### STEP 1 — Remove the Mouse Feet (Skates)

> **Why:** The 4 Phillips screws are hidden underneath the rubber PTFE feet.

1. Use your plastic pry tool or fingernail to **peel up the rubber feet** from the bottom of the mouse.
   - There are 2 large pads — one at the front, one at the rear.
   - Peel slowly from one corner. They will stretch but should come off in one piece.
   - **⚠️ The original feet will be damaged/warped after removal. The customer has brought replacement feet — do not try to reuse the originals.**
2. You will now see **4 Phillips screws** (2 under front pad area, 2 under rear pad area).

---

### STEP 2 — Open the Shell

1. Unscrew all 4 Phillips screws with your #00 screwdriver. Set aside safely.
2. Gently lift the **top shell** (the part with the buttons) away from the **bottom shell** (the part with the battery compartment).
3. **⚠️ STOP before fully separating** — there is a **ZIF ribbon cable** and a **battery wire** connecting the two halves.
   - The ribbon cable connects the top button assembly to the main board.
   - The battery wire connects the AA battery compartment to the main board.
4. Lay the top shell flat beside the bottom shell. **Do not yank or stretch the cables.**

---

### STEP 3 — Disconnect the Ribbon Cable (Optional but Recommended)

> This gives you full freedom to work without straining the cable.

1. Locate the **ZIF (Zero Insertion Force) ribbon connector** on the main board — it looks exactly like the ones you disconnect on phone screens.
2. Use a fingernail or plastic spudger to **flip up the small locking tab** on the connector.
3. Slide the ribbon cable out gently.
4. Also **unplug the battery wire** connector (it's a small 2-pin JST-style plug — just pull it straight out).

---

### STEP 4 — Remove the Switch Daughterboard

1. Inside the top shell, you will see a **small PCB** (approximately 3cm × 2cm) with the left click switch and right click switch mounted on it.
2. This board is held by **2 small Phillips screws** — remove them.
3. Lift the daughterboard out. This is the only PCB you will be working on.

---

### STEP 5 — Desolder the Faulty Switch(es)

> **Replace BOTH left and right click switches** even if only right is dead — so the click tension matches and you won't be called back for the left one in 3 months.

1. Look at each switch — it has **3 pins going through the PCB**.
   - The switch body sits **on top** of the PCB.
   - The 3 pins poke through from above and are soldered on the **underside** of the PCB.
2. Flip the daughterboard over to see the 3 solder joints per switch.
3. Apply a tiny amount of **flux** to each joint.
4. Use your **desoldering pump** or **solder wick** to remove solder from all 3 pins of the switch.
   - **Temperature:** Set iron to **320°C–340°C** (same range as phone board work — do not go higher).
   - Work quickly on each pin — the pads are small but robust.
5. Once all 3 pins are clear, the switch body will lift straight off the top of the PCB.
6. Repeat for the second switch.
7. Clean both pad areas with IPA + cotton swab.

---

### STEP 6 — Install the New Switches

> **This is the most important step — read carefully.**

1. Take the new replacement switch (Kailh GM 8.0 or equivalent).
2. Identify the **orientation** — the switch has a small **rectangular plunger/actuator** on top. This actuator must face **toward the front (nose) of the mouse** — i.e., toward where your fingertip presses.
3. Insert the switch **from the top** of the daughterboard, pushing the 3 pins through the 3 holes.
4. **Crucial — Flush Seating:** Press the switch body completely flat against the PCB surface before soldering. If it sits even 0.5mm tilted or raised, the mouse button paddle above it won't align and the click will feel mushy or won't register.
   - **Tip:** While holding the switch down with a finger from the top, tack one pin on the underside with a small dot of solder to lock it in position. Then check it's flush before completing all 3 joints.
5. Solder all 3 pins cleanly. Good joints should be shiny and cone-shaped — not blobby or dull.
6. Repeat for the second switch.

---

### STEP 7 — Test BEFORE Reassembly

> **Do not close the mouse without testing first — same rule as phones.**

1. Reconnect the ribbon cable (flip the ZIF tab back down) and plug the battery wire back in.
2. Insert the AA battery and **power the mouse ON** (switch on the bottom).
3. Plug the USB receiver into a nearby computer.
4. Click both the left button and right button physically — you will hear a sharp, crisp click.
5. Verify both clicks register on screen (open Notepad or a browser — left click selects, right click shows context menu).
6. **If both work:** Proceed to reassembly.
7. **If one doesn't work:** Check solder joints on that switch — most likely a cold joint or a pin that didn't fully make contact.

---

### STEP 8 — Reassemble

1. Power the mouse OFF.
2. Remove the battery.
3. Reinsert the daughterboard into the top shell — replace its 2 screws.
4. Reconnect the ribbon cable and battery wire (if you disconnected them earlier).
5. Carefully align the top shell back onto the bottom shell.
6. Replace the 4 Phillips screws on the bottom.
7. **Apply the new PTFE mouse feet** provided by the customer:
   - Wipe the bottom surface first with IPA to remove any oil or residue.
   - Peel the adhesive backing and press the new feet firmly into the recessed slots on the bottom of the mouse.
   - Press down and hold for 10 seconds per pad.

---

### STEP 9 — Final Verification

1. Insert battery, power ON, reconnect USB receiver.
2. Test left click + right click one more time on a computer.
3. Place the mouse on a flat surface — it should sit evenly on all 4 new feet with no wobble.
4. Hand back to customer.

---

## 🚫 Common Mistakes to Avoid

| Mistake | Why It Happens | How to Avoid |
| :--- | :--- | :--- |
| Switch sitting tilted | Pins inserted but not fully pressed flat before soldering | Tack one pin, visually confirm flush, then complete |
| Cold solder joint → click doesn't register | Iron not hot enough or moved too fast | 320–340°C, hold 2–3 seconds per pin until solder flows |
| Ribbon cable kinked or torn | Forgot it was there when separating shell halves | Disconnect ribbon FIRST before fully opening shell |
| Lifted PCB pad | Too much heat or mechanical pull while desoldering | Use flux, pump cleanly — don't lever the switch off |
| Mouse wobbles after repair | Old deformed feet reused instead of new ones | Always apply the new feet provided |

---

## 💬 What to Say to the Customer When Done

> *"Switch replaced on both left and right click. Used premium switches — rated 80 million clicks. Tested both buttons. Working perfectly. New feet applied. Should last several more years."*

---

**Total Labour Charge (Suggested):** ₹200 – ₹350
*(Switch replacement is simpler than a phone motherboard job — 3 large through-hole pins vs. SMD micro-components)*
