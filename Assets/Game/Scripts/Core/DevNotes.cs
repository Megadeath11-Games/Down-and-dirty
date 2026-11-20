using UnityEngine;

namespace FPSBase.Core
{
    /// <summary>
    /// The dev blog (main menu) and the "what to test" checklist shown on the left of the screen. Update both with
    /// every build: bump <see cref="TestListVersion"/> when the checklist changes so the ticks start fresh.
    /// </summary>
    public static class DevNotes
    {
        public struct Post { public string date, title, body; }

        /// <summary>Newest first.</summary>
        public static readonly Post[] Blog =
        {
            new Post { date = "Update 29  ·  Nov 20, 2026", title = "A real dig site behind the main menu",
                body =
                "• The main menu now shows the real game: a 20 t excavator digging a trench and loading a haul truck, the truck tipping its load, and a dozer pushing a berm, all with the same dirt as when you play (the bucket cuts and scoops, clods fall and roll, piles slump).\n" +
                "• When you start or load a game, the machines pack up and every bit of ground they touched goes back as it was. Nothing they dig carries into your world, and no gold or rocks get uncovered." },
            new Post { date = "Update 28  ·  Nov 20, 2026", title = "Pressing down, no spikes, wide pours, quick invites",
                body =
                "• Pressing the bucket down flat (to take the weight off the tracks and turn) no longer shoves the dirt round and round under it, which lagged the game. It packs the dirt and lifts the machine.\n" +
                "• No more spikes of dirt in front of the bucket: clods that come to rest spread over their own size instead of standing up in one 12.5 cm square, the big buckets leave no uncut columns, and dirt that won't fit spreads across the mouth.\n" +
                "• Skid steers, loaders and excavators pour out along the whole edge of the bucket, not just from the middle.\n" +
                "• Multiplayer: once your hosted game has loaded it asks who you want to invite, and friends with the game open are at the top of the list." },
            new Post { date = "Update 27  ·  Nov 20, 2026", title = "A full bucket keeps pulling",
                body =
                "• A full excavator bucket no longer stops dead in the dirt like it hit concrete. Drag it in with the stick or curl and it keeps cutting: what it slices off heaps up in front of the mouth and gets pulled (or pushed) along with the bucket, so you can still drag dirt towards you to clean up or back-fill." },
            new Post { date = "Update 26  ·  Nov 20, 2026", title = "Concrete blocks, a bucket chain and two giant machines",
                body =
                "• Concrete wall blocks: the landscape supply now has a block yard next door selling precast blocks in six sizes (half 2×2×2 ft, the 2×2×4 ft lock block, long 2×2×6 ft, big 2.5×2.5×5 ft, cap 1×2×4 ft and the 3×3×6 ft barrier). Back a trailer or a dump truck into the painted bay, buy at the office, and they load them on for you.\n" +
                "• Chain them down (look at a block, F: one block or the whole load) before you drive off. Loose, they slide about.\n" +
                "• Every excavator has a lifting chain on the bucket linkage: H lets it down, put the hook by a block's loop and press H again to hook it, lift and swing it over, set it down and H lets it go. Unload them one at a time and stack them.\n" +
                "• Blocks hold back dirt: stand a row on the ground and pile dirt behind it and it stays behind the wall instead of slumping through. Build retaining walls on a hillside.\n" +
                "• New: the Mining Shovel 150 t, a massive excavator with a 12.5 m³ bucket, and the Mining Haul Truck 60 t. Both at the dealer.\n" +
                "• The wheel loader and the dump trucks are rebuilt with the same detailed look as the rest of the fleet.\n" +
                "• Dozers don't bounce: cutting with the blade down, the ground's push on the blade was jumping about from moment to moment and the machine porpoised along its cut, worst on the mining dozer. It's smooth now.\n" +
                "• A bit lighter to run: the trees, bushes and rocks round the map were drawn one by one (a third of everything drawn). Now they're drawn in big batches, 40% fewer things to draw. Redrawing the dug ground takes less of each frame while a friend is pushing dirt. (Two-player dozing and digging is still heavy: more to come.)\n" +
                "• Blocks are saved with your world (chained ones stay chained) and show up for your friends in multiplayer.",
            },
            new Post { date = "Update 25  ·  Nov 20, 2026", title = "Smooth graded roads and clean cut walls",
                body =
                "• The grader cuts real roads: the moldboard spreads the dirt it carries into the low spots behind it and cuts to the machine's height averaged over the last few metres (it hangs between the front wheels and the tandems), so one pass over lumps and holes leaves an even road with a windrow at the side. A shallow skim shows as dirt, not grass, and the fill is packed so your tyres don't rut it.\n" +
                "• Smooth ground is drawn smooth: a graded road or a trimmed floor no longer gets the lumpy look of freshly dug dirt.\n" +
                "• Cut walls look right: the walls of ditches, pits and dozer cuts are drawn as clean faces instead of saw teeth and long stretched streaks, and a deep pit wall shows its layers (topsoil, clay, sand) in bands. No more grass showing through at the corners.\n" +
                "• Sitting in a machine on a slope, you lean with it instead of staying bolt upright (your friends too).\n" +
                "• Fixed: bits of dirt shooting straight up out of the ground around a digging excavator (they were starting out under the dirt and popping up to the top).",
            },
            new Post { date = "Update 24  ·  Nov 20, 2026", title = "Dozers bite in, and a friend's pushing doesn't lag you",
                body =
                "• Dozers cut properly: lower the blade and drive in and it takes the topsoil as deep as the blade is set, all the way along. Before, the heap building up in front of the blade held the cut up and the ground lifted the machine instead.\n" +
                "• Skid steers: tip the bucket down and drive into the dirt and the edge cuts in, keeping the dirt while it's cutting. Lift it out still tipped down and it pours.\n" +
                "• Fixed: watching a friend push dirt lagged badly. Your game made 16 times as many clods for their blade as for your own (one for every small square of the finer ground); now it's the same as yours.",
            },
            new Post { date = "Update 23  ·  Nov 19, 2026", title = "You have to scoop: and the ground is twice as fine",
                body =
                "• Shoving the bucket straight down as hard as it goes no longer digs a hole or fills it, and no dirt sprays everywhere. The teeth only creep in a little and the ground pushes the machine up.\n" +
                "• To fill the bucket you have to drag it through the dirt with the stick or the curl, cutting edge first. The dirt comes in at the mouth where it's cut, not popping up inside.\n" +
                "• An opened-out bucket only holds a little (under half): curl it in to fill it. Dirt stays packed in while you're cutting and while the bucket's still down in its hole, and pours out when you lift it out opened.\n" +
                "• The ground is now in 12.5 cm squares (was 25 cm): ditches, holes and heaps show far more detail.\n" +
                "• And it's faster than before: the ground near you redraws in quarters so digging only redraws the bit you're in, and a slow step in updating the ground you drive on (about 12 ms every time you dug) is gone. Pushing dirt with a dozer runs about as fast as before on the twice-as-fine ground, standing by is faster.\n" +
                "• Fixed: dirt the bucket heaped over, or shoved while opened out, was dropped right on top of the bucket.",
            },
            new Post { date = "Update 22  ·  Nov 19, 2026", title = "The bucket digs exactly where it goes",
                body =
                "• The excavator bucket now cuts the ground in its own shape: every part of the bucket, the teeth, the floor, the back and the sides, takes out the dirt it moves through. No more bucket sinking into dirt that's still drawn, and the hole it leaves is the shape of the bucket.\n" +
                "• It really scoops: dirt only goes into the bucket when the cutting edge leads (curling, crowding, dragging in), and the clumps come in at the mouth where it was cut, instead of appearing at the back.\n" +
                "• Curl out into a pile, swing sideways or push with the back of the bucket and it shoves the dirt along like a small blade. Nothing jumps into the bucket.\n" +
                "• The teeth bite into hard ground the way they point, and they have their own hitbox now, so they don't poke through things.\n" +
                "• The ground pushes back: force the bucket, the stick or the boom into hard ground faster than it can cut and the arm stalls and the machine lifts up on it, like a real one. Lower a dozer or 5 t blade into the dirt and it lifts the front, the same as on pavement.\n" +
                "• Fewer clumps: far fewer clods drawn on loose dirt, no clods flicked up by the teeth, fewer off a dozer's heap.\n" +
                "• Clods keep their size: what falls out of a bucket, a loader or a truck bed lands as big as it was inside, instead of turning into little ones.",
            },
            new Post { date = "Update 21  ·  Nov 19, 2026", title = "Dug dirt you can see the detail in",
                body =
                "• Loose dirt now slides off in every direction, diagonals too, so heaps and the sides of holes come out rounded and natural instead of square pyramids with big flat faces.\n" +
                "• Dug and dumped dirt is drawn lumpy, a few centimetres up and down at each point, so the small 25 cm facets show instead of looking like one smooth plane. (Only the look: what you dig and drive on is the same.)\n" +
                "• Fixed: digging from inside a machine redrew the ground you dug in big 1 m triangles until you got out (it went by the wrong camera). It stays fine and detailed while you dig now.\n" },
            new Post { date = "Update 20  ·  Nov 19, 2026", title = "Finer ground: straight ditches and holes",
                body =
                "• The dirt is now in 25 cm squares instead of 50 cm, so a bucket digs ditches and holes with walls that go straight down (1 m deep right next to untouched ground).\n" +
                "• Firm ground holds a straight wall: topsoil up to about 1.5 m, clay higher. Sand and gravel still cave in, and firm ground over a sand seam falls in when the sand runs out from under it.\n" +
                "• Faster: far-off ground is drawn with fewer facets (and fills in as you get close), the ground near a working blade redraws quicker, and the clods bump into each other far more cheaply. Frame rate is up and the worst hitches are about half as long.\n" +
                "• Your saved worlds load onto the finer ground with everything you dug kept as deep as it was.\n" },
            new Post { date = "Update 19  ·  Nov 19, 2026", title = "Dirt rolls in clods in front of the blade",
                body =
                "• Pushing dirt with a dozer or grader now cuts the ground into small clods that roll and tumble in front of the blade, pile up against each other and spill off the ends, then settle back into the ground where they stop. No more dirt jumping from one square to the next.\n" +
                "• The ground redraws near you first and in small pieces each frame, so the cut follows the blade smoothly.\n" +
                "• The blade no longer digs itself in: when the nose dips into the cut it keeps cutting at the depth you set, instead of diving deeper and deeper.\n" +
                "• Multiplayer: when your friend pushes dirt you see their clods rolling in front of their blade, and the ground you both end up with is the same. Small dirt piles show up for everyone too.\n" },
            new Post { date = "Update 18  ·  Nov 19, 2026", title = "Town ground fixed, trench markers follow the ground",
                body =
                "• The town's ground was built wrong: the hills from the edge of town were squeezed in toward the yards (so some lots sloped), and a strip along East Ave and the north edge sat 8 m down. It now lies flat in town and meets the hills properly. Your saved worlds are moved onto the fixed ground when you load them, with everything you dug kept as deep as it was.\n" +
                "• The blue trench marker follows the ground along its length (on a slope it used to float level over the low end), and the job is measured the same way: dig that deep below the ground at each point.\n" },
            new Post { date = "Update 17  ·  Nov 19, 2026", title = "Trailer decks move right away for everyone",
                body =
                "• When a friend tilts a trailer's deck or works its ramps, it starts moving on your screen straight away (it used to wait for the trailer's next update, up to half a second plus the lag).\n" +
                "• Tilting or levelling the deck of a trailer hitched to a friend's truck now works: before, their game put it straight back.\n" },
            new Post { date = "Update 16  ·  Nov 19, 2026", title = "Options on the main menu",
                body =
                "• The main menu has an Options button: set the sound and music volumes, graphics, the camera and mouse, the excavator controls and every key binding before you load into a world. It's the same settings as the pause menu, and they're saved for next time.\n" },
            new Post { date = "Update 15  ·  Nov 19, 2026", title = "Down and Dirty, and every excavator function at once",
                body =
                "<b>New</b>\n" +
                "• The game is called Down and Dirty everywhere now: the window, the exe (Down and Dirty.exe) and the download. Your saved worlds come over from the old name by themselves the first time it starts.\n" +
                "• Excavator: the machine panel shows what's moving right now (\"stick in + curl + swing left\"). Stick, swing, boom and bucket have always worked all at once in the game; if a combination doesn't take, it's the keyboard: many can't register some three- or four-key combinations, especially I/J/K/L held with W/A/S/D. The number pad works the right hand too (8/5 boom down/up, 4/6 curl/open), and is usually wired apart, so all four go together. A controller does all four at once as well.\n" },
            new Post { date = "Update 14  ·  Nov 19, 2026", title = "Solid multiplayer, a working main menu, lofi beats and real machine sounds",
                body =
                "<b>Multiplayer</b>\n" +
                "• Tested for hours with scripted players in 2 and 3 player games, on a perfect connection and on a bad one (up to a quarter of a second of lag with 15% of the updates lost): walking, driving, digging the mine and the town, buying machines, loading and dumping trucks, the wash plant and the gold, crew jobs and their pay, towing, two people grabbing the same seat, leaving and rejoining, and saving, loading and hosting again. Everyone's world matched every time.\n" +
                "• Friends' machines move smoothly even on a laggy connection: they're drawn a moment behind, between the updates they send, so a late or lost update no longer makes them stop and start.\n" +
                "• Dirt piles are shared: piles a friend dumps (and digs away) are the same in your game, and a friend joining gets the piles already there.\n" +
                "• When the host closes the game, friends go back to the main menu with a message (before, they were left in a frozen copy of the world). With three of you, the game ends for both friends, rather than one of them quietly taking over a copy.\n" +
                "• Joining a friend while you're in your own world loads their world fresh, and leaving their game takes you back to the main menu, so you never carry a stale copy into the next game.\n" +
                "• When a friend leaves, their machines stay where they parked them, run by the host (before, every player tried to take them over).\n" +
                "<b>Main menu</b>\n" +
                "• Behind the menu, a crew at work: an excavator digs a pile and loads dump trucks that take turns, while a dozer keeps pushing dirt up to it.\n" +
                "• Every menu now has the same look as the equipment dealer: the main menu, the pause menu and its settings, the key bindings, the machine panels, the messages and the what-to-test list.\n" +
                "<b>Music</b>\n" +
                "• Five chill lofi tracks, made for the game (so they're free to stream and share), shuffled with a crossfade. They play in the menus and while you play, a little quieter when you're in a machine. Esc > Audio: music volume, music while playing on or off, and Next song.\n" +
                "<b>Sound</b>\n" +
                "• New machine sounds: the diesel has an idle and a working sound and revs between them; the hydraulic pump whines while the arm, blade or bed moves; it groans when the bucket digs hard or a ram is held at the end of its travel; the blade scrapes as a dozer or grader cuts; tracks clank as they roll; the bucket's teeth scrape through the dirt; and dirt and gravel rattle as they pour out of a bucket or a truck bed. Friends' machines are heard too.\n" +
                "• Pausing a single-player game stops the world's sounds (the music carries on).\n" },
            new Post { date = "Update 13  ·  Nov 19, 2026", title = "Trenches of every kind, careful digging, a town that isn't copy-paste",
                body =
                "<b>New</b>\n" +
                "• Trench jobs vary: a drainage ditch down the side of the lot, a ditch along the front yard, a trench across the back yard, a drain pipe trench from the downspout, or a trench along the side of the house. Each one has its own length (about 5 to 18 m), depth (40 cm to 1.2 m) and width (40 cm to 1 m), and the pay follows the size. Driveways ask for different gravel thicknesses too (5 to 12 cm).\n" +
                "• Be careful on their lot. Hit the house, the mailbox, a fence, the hedge, the shed, a tree or the porch with a machine (or a trailer) and the customer takes a share off the bill to fix it (10% for a mailbox, 20% for the house...). Digging right up against the foundation, under the mailbox post or into a tree's roots counts too. The customer's text warns you what's close to the work. Mailboxes and fence sections get knocked over, and it's saved and shows for your friends. Wreck something where you don't have a job and they send you the bill.\n" +
                "• The trench is marked with a see-through light blue box exactly as wide and as deep as it has to be dug, with an orange outline on the ground. A driveway shows a blue slab as thick as the gravel has to be.\n" +
                "• Finding the house: an orange beacon stands over every job's house, the job's text tells you which way to turn coming into town and which house it is (\"turn right on West Ave, then left onto Birch Ln. Number 111 is the sixth house on your left\"), there are street signs at every corner, and a welcome board where the road comes in lists your jobs and the turn for each.\n" +
                "• The town is all different now: houses of different sizes and depths, one or two storeys, gable, hip or flat roofs, single or double garages, 12 wall colours and 6 roof colours, porches, shutters, flower beds, chimneys, picket or wood fences, hedges, garden sheds and different trees.\n" +
                "<b>Fixed</b>\n" +
                "• A driveway passes when the gravel is laid to its thickness, give or take a centimetre and a half.\n" },
            new Post { date = "Update 12  ·  Nov 19, 2026", title = "Dump truck hitches, readable prompts",
                body =
                "<b>New</b>\n" +
                "• Both dump trucks (the single-axle and the 25 t haul truck) have a pintle hook under the back bumper. Tow the tilt-deck or gooseneck trailer with a skid steer on it behind a load of gravel, unhitch at the customer's, dump the gravel on the drive and spread it.\n" +
                "<b>Fixed</b>\n" +
                "• The text you get when you look at something (like the landscape supply bin) no longer spills out of its box: it sits on a dark backing and wraps neatly when it's long.\n" +
                "• The bucket / dump box bar at the bottom grows to fit its message, so BUCKET FULL and its tips aren't cut off.\n" +
                "• The mouse stays inside the game window while a menu, the map or the phone is open, so it can't wander onto another monitor.\n" },
            new Post { date = "Update 11  ·  Nov 19, 2026", title = "Crew jobs, a real map and a minimap",
                body =
                "<b>New</b>\n" +
                "• Jobs in multiplayer: everyone sees the same customers and texts. When you accept a job you pick <b>Just me</b> (it's yours and you get all the pay) or <b>Share with crew</b> (anyone can work it and finish it, and the pay is split evenly between everyone in the game). Your friends' answers show in the conversation with their names, and a friend's own job shows who took it.\n" +
                "• A much more detailed map (M): the real ground seen from above, shaded hills with contour lines, the roads, the buildings, the trees, the houses in town with their numbers and the street names, and the dig grounds coloured by how deep they're dug (piles show lighter). Scroll to zoom, drag to move it, or use the buttons to zoom, centre on you or see the whole map.\n" +
                "• Every machine has a little top-down icon on the map that looks like it (excavators, dump trucks, pickups, loaders, skid steers, dozers, graders, semis, trailers) and turns the way it's facing. Players are arrows pointing where they're looking: you in red, friends in blue with their names.\n" +
                "• A minimap in the bottom right that follows you, with the phone's new-message notice just above it.\n" +
                "<b>Changed</b>\n" +
                "• The compass at the top is tidied up: N, E, S, W and the in-betweens, a small pin for each place, and the names and distances underneath in two neat rows. The places closest to where you're looking get their names first, and names never pile on top of each other.\n" +
                "• The movement hints in the top left no longer print over the map / help line.\n" +
                "<b>Fixed</b>\n" +
                "• Multiplayer: a machine chained on a friend's trailer no longer spins its tracks or wheels on your screen.\n" +
                "• Multiplayer: a friend's trucks, trailers and the machines on them no longer go, stop dead and speed up again. They keep moving smoothly between updates.\n" },
            new Post { date = "Update 10  ·  Nov 19, 2026", title = "The town: customers, your phone, gravel jobs",
                body =
                "<b>New</b>\n" +
                "• A town over the hill east of the mine: follow the new road from the heavy-equipment yard through the pass. Three streets (Birch Ln, Maple St, Cedar Ave) with 48 houses, each with a driveway, a yard and a mailbox.\n" +
                "• Your phone (P, or D-pad left on a controller). People in town text you when they need work done: a gravel driveway or a drainage ditch down the side of their lot, and what they'll pay. Accept or decline right in the conversation.\n" +
                "• Accepted jobs are staked out with orange flags and pinned on the map (M). The phone shows how far along each job is, and once it passes you tap Finish & get paid. Neat work gets a tip.\n" +
                "• Gravel driveway: dump gravel on the drive and spread it out with a skid steer or loader, at least 6 cm over 90% of it, with no pile over 30 cm left. Drainage ditch: dig it 0.6 m deep along the stakes.\n" +
                "• Landscape supply on the south side of town: back a truck under the bin and buy gravel, sand or topsoil by the cubic metre. It pours into the bed.\n" +
                "• The yards in town are diggable ground like the mine (you can't dig under the houses or the streets). Machines, dumping, the metal detector and multiplayer all work there, and the town is saved with your game.\n" },
            new Post { date = "Update 9  ·  Nov 19, 2026", title = "New shop, deep digging, fast dumps",
                body =
                "<b>New</b>\n" +
                "• The equipment dealer is redone: categories down the side, a picture of every machine, and hover over one for what it is, its price and its full spec sheet (weight, size, bucket or box, speed, fuel, hitches).\n" +
                "• A dragged bucket reshapes undug ground: open the bucket and pull it across the grass and it shaves the high spots and pushes the dirt along, like grading with a real excavator.\n" +
                "<b>Changed / fixed</b>\n" +
                "• Wash plant upgrades are saved with the game.\n" +
                "• Dig much deeper: bedrock on the claim is now 12 m down (was 4.5 m), and off the claim you can go 6 m. The 90 t reaches about 7 m below its tracks, all of it diggable. The gravel keeps getting a little richer down toward bedrock.\n" +
                "• Dump trucks: a fully open tailgate lets the load rush out in about 4-5 seconds, whatever the size of the truck (the big ones took half a minute).\n" +
                "• Controller: with a menu or shop open the sticks only move the pointer, your character stays put.\n" +
                "• Controller, excavators: hold RT or LT to drive the tracks and steer them with the left stick. The D-pad still spins on the spot.\n" },
            new Post { date = "Update 8  ·  Nov 19, 2026", title = "Controller support, wide lowboys, dump marker",
                body =
                "<b>New</b>\n" +
                "• Controller support (Xbox / PlayStation pads). Triggers for gas and brake, sticks for steering and the machine controls, with proportional control. On menus and in shops the left stick moves the pointer and A clicks. The whole layout is under Help (F1).\n" +
                "<b>Changed / fixed</b>\n" +
                "• RGN lowboys are wider (25 t: 3.4 m, 55 t: 4.3 m, 95 t: 5.2 m), so the 50 t, 70 t and 90 t excavators' tracks sit on the deck instead of slipping off the edge.\n" +
                "• Excavator dump marker: the green ring now shows where the load lands once the bucket opens, not under the teeth (they were up to 1.7 m off with the bucket curled).\n" +
                "• Multiplayer: you see the dirt falling out of a friend's dump truck, bucket or loader when they dump.\n" +
                "• Multiplayer: a machine gets chained on a trailer even while your friend sits in the truck.\n" +
                "• Haul Truck 25t (and the single-axle truck): low gears pull hard at a crawl, and the tandem's diffs come locked. Loaded, it climbs an 18° hill forwards or backing up (it used to stall halfway).\n" },
            new Post { date = "Update 7  ·  Nov 19, 2026", title = "Multiplayer: trailers, wheels, wash plant",
                body =
                "<b>Multiplayer fixes</b>\n" +
                "• Trailers stay in sync: whoever drives the truck runs its trailer, and everyone else sees it follow on the hitch, wheels rolling, deck tilting, ramps and gooseneck moving.\n" +
                "• A parked trailer belongs to whoever used it last; hitch it or use its menu and it's yours.\n" +
                "• Trailers bought on their own (bowser, tilt deck, gooseneck) now show up for everyone, and after loading a save.\n" +
                "• Friends' pickups and dump trucks roll their wheels on your screen.\n" +
                "• The wash plant is shared: the host's plant does the washing, and everyone sees what's in the hopper and the sluice. Dump into it on any game and it goes in. Cleaning up the sluice pays whoever cleans it.\n" +
                "• A friend's gooseneck ball shows in their pickup's bed.\n" +
                "<b>Also fixed</b>\n" +
                "• Haul Truck 25t: the back axle was stuck under the truck and didn't drive. Both rear axles show and pull now.\n" +
                "• Fuel bowser (and every trailer): \"Set it back upright\" shows whenever it's leaning over, hitched or not. Hitched, it stands back up with its ring on the hook.\n" +
                "• Repositioning or towing a truck that's rolled on its side keeps the way it was pointing, and its trailer comes back upright behind it.\n" },
            new Post { date = "Update 6  ·  Nov 19, 2026", title = "Trailer fixes, $75 gold, camera zoom",
                body =
                "<b>Changed / fixed</b>\n" +
                "• Tilt-deck trailer: you can tilt the deck with a machine chained on; it tilts with the deck.\n" +
                "• Gooseneck trailer: both ramps are there now (the second one was missing).\n" +
                "• Dump box tailgates swing out the back as the box tips, instead of into the box.\n" +
                "• Pickup: letting off the gas coasts instead of braking hard; fixed the wheels locking up around 18 km/h with a heavy trailer on.\n" +
                "• Trailer brakes only come on when you brake (or stop): no more dragging over bumps and round corners.\n" +
                "• A chained machine's wheels no longer drag the trailer along.\n" +
                "• The pickup pulls the 5 t excavator on the tilt trailer in 2WD on flat ground (about 30 km/h in 8 s).\n" +
                "• A tipped trailer stays put instead of gliding along on its side, and its menu can set it back upright.\n" +
                "• Repositioning or towing a truck brings its trailer back upright, straight behind it.\n" +
                "• Gold sells for $7,500 a gram ($75 per 0.01 g).\n" +
                "• Pickup cab: your feet, the pedals and the seat no longer stick out under the truck.\n" +
                "• Driving: switching to first person (V) looks straight ahead; the scroll wheel zooms the third-person camera.\n" +
                "• Tracked machines no longer creep sideways down slopes and heap flanks, parked or driving.\n" +
                "• An angled blade carries more of its load, so less rolls off the side.\n" +
                "• Loader buckets fill at any angle short of dumping (the HUD's \"flat\"), and fill when you shove into a pile even if it stops you.\n" },
            new Post { date = "Update 5  ·  Nov 18, 2026", title = "Equipment trailers, heavy dirt and proper towing",
                body =
                "<b>New</b>\n" +
                "• 7 t tilt-deck equipment trailer (bumper-pull, $9,500 at the dealer). Hitch it to the pickup's hook, tilt the deck down from its menu, drive a skid steer on over the back, then level the deck and the machine is chained down.\n" +
                "• 10 t gooseneck equipment trailer ($14,500) with folding ramps at the back: lower them from its menu, drive on, park, fold them up.\n" +
                "• Gooseneck ball for the GMT400: fit it from the pickup's menu (dump box empty and down). It locks the dump box while it's in. Back the truck in under the gooseneck's coupler to hitch.\n" +
                "• Both trailers are built for the three skid steers and the 5 t compact excavator. Anything else isn't chained down.\n\n" +
                "<b>Changed / fixed</b>\n" +
                "• Dirt in buckets and truck beds feels heavy now: it sits still over bumps and gentle starts, shifts with a thud on a hard jolt, and doesn't bounce about like packing peanuts.\n" +
                "• Skid steers keep their load: a full bucket carried low and curled back loses only a few percent through a hard reverse.\n" +
                "• A tipped bucket still pours freely.\n" +
                "• Trailers have their own brakes: the truck's brake controller brakes the trailer to match, so a full fuel bowser no longer shoves the pickup into a spin or a jackknife when you slow down. Stopped, they hold it; pull away and they let go.\n" +
                "• Pickup anti-lock brakes: the wheels don't lock and slide out under hard braking.\n" +
                "• Hitched trailers collide with the truck again (only the tongue at the hitch passes through), so you can't drive into your own trailer.\n" +
                "• The bowser's tandem axles have softer, longer-travel springs and the hitch pitches further, so the rig flexes over humps." },
            new Post { date = "Update 4  ·  Nov 18, 2026", title = "Blade tilt, blade angle, new skid steers",
                body =
                "<b>New</b>\n" +
                "• Dozer blade tilt: R drops the left end, T drops the right end. Good for cutting a ditch or crowning a road.\n" +
                "• Dozer blade angle (swivel): G swings it left, H swings it right. An angled blade rolls the dirt off its trailing end into a windrow beside you.\n" +
                "• All three dozers have it (the mining dozer swivels a bit less).\n" +
                "• GMT400 pickup: X switches 2WD / 4-HIGH / 4-LOW, Z locks the rear diff. 4-low pulls a full fuel bowser up a steep hill.\n" +
                "• Heavy machines pack loose dirt down under their tracks: cross a berm with the 66 t dozer and its ruts sink in. Light machines hardly pack it.\n\n" +
                "<b>Changed</b>\n" +
                "• Skid steers rebuilt: the stand-on, the wheeled skid steer and the compact track loader all have new bodies, cabs, lift towers, longer two-piece arms and a deeper low-back bucket.\n" +
                "• Track loaders get proper undercarriages: drive sprocket with teeth, idlers and bottom rollers.\n" +
                "• Machines no longer creep down hills when parked or with you in the seat (tracks grip up to about 42°).\n" +
                "• Heavy machines ride smoother and heavier over rough cuts; the 1 t loader and mini excavator stay twitchy and tippy.\n" +
                "• The pickup has a bit more power and its tyres keep their grip when they spin; W while rolling back drives instead of braking.\n" +
                "• Fixed: after unhitching the fuel bowser, the truck could drive straight through it.\n" +
                "• The bowser now collides with everything again once you pull away." },
            new Post { date = "Update 3  ·  Nov 18, 2026", title = "Saved worlds, a main menu, and heavy haul",
                body =
                "<b>New</b>\n" +
                "• A main menu: Single Player (start a new world, or load / delete a saved one) and Multiplayer (host a new or saved world over Steam, or join a friend).\n" +
                "• Save your world from the pause menu (Esc). Single player saves and multiplayer saves (made by the host) are kept apart. A save keeps the dug ground, every machine and trailer (where they are, fuel, the pickup's tidy tank and dirt in the box, a lowboy's gooseneck on or off, a bowser's fuel and what it's hitched to), dirt piles, your money and gold, and where you stand.\n" +
                "• Main Menu button in the pause menu (it asks first: unsaved progress is lost).\n" +
                "• Semi trucks: 25 t, 55 t and 95 t, each with a removable-gooseneck (RGN) lowboy. Detach the neck, the deck drops, drive a machine on, park it and it's chained down; hook the neck back on and haul it.\n" +
                "• The starter truck is now a GMT400 3/4-ton 4x4 pickup with a removable 420 L tidy tank in the box.\n" +
                "• A 3,500 L field fuel bowser trailer: hitch it to the pickup, unhitch it on site, refill it at the pump from the scroll menu.\n" +
                "• This dev blog, and a test checklist on the left of the screen.\n\n" +
                "<b>Changed / fixed</b>\n" +
                "• The scroll menu on machines: the selected option is readable now (same size, bold) and the box fits long options.\n" +
                "• Machines chained on a trailer no longer spin their tyres or roll their tracks as the trailer moves.\n" +
                "• Removed the \"scroll to choose\" hint under the machine menu." },
            new Post { date = "Update 2", title = "In the cab",
                body =
                "<b>New</b>\n" +
                "• First-person cab view shows your own arms and legs working the joysticks, levers and pedals." },
            new Post { date = "Update 1", title = "Machine menu, towing and fuel",
                body =
                "<b>New</b>\n" +
                "• Look at a machine and scroll for its menu: refuel from a tidy tank, reposition it, or pay for a tow anywhere on the map.\n\n" +
                "<b>Fixed</b>\n" +
                "• Dealer deliveries land on clear ground.\n" +
                "• Dozer berms build up properly in front of the blade." },
        };

        public const string TestListVersion = "2026-11-20f";

        /// <summary>What's new in this build to try out.</summary>
        public static readonly string[] TestList =
        {
            "Main menu: watch the machines behind it for a minute: the excavator digs and loads the truck, the truck tips, the dozer pushes dirt",
            "Main menu: start a new game and walk over to where the menu machines were working: the ground is untouched",
            "Excavator: hold the boom down with the bucket flat on dug dirt: the machine lifts so you can turn, no lag, the dirt doesn't get shoved about",
            "Excavator (try the 90 t): dig a few buckets: no spikes of dirt left standing in front of the bucket",
            "Skid steer: dump a full bucket: it pours out along the whole edge, not one stream from the middle",
            "Multiplayer: host a game: when it has loaded, it asks you to invite friends, with friends in the game at the top",
            "Excavator: fill the bucket, keep it in the dirt and pull the stick in: it keeps moving and drags the dirt towards you instead of stopping dead",
            "Block yard (next to the landscape supply): back a trailer into the painted bay, buy some lock blocks: they're loaded onto the trailer",
            "Look at a block on the trailer, F: chain down the whole load, then drive off: they ride with the trailer and don't slide",
            "Excavator: press H to let the chain down, put the hook by a block's loop, H again: it hooks on; lift and swing it, set it down, H lets it go",
            "Stack blocks into a wall on a slope and pile dirt behind it with the excavator or dozer: the dirt stays behind the wall",
            "Save with blocks on a trailer and some on the ground, load it: they're all back, the chained ones still chained",
            "Multiplayer: buy blocks and unload them with the chain: your friend sees them load, ride, swing and land",
            "Dealer: buy the Mining Shovel 150 t and the Mining Haul Truck 60 t: dig and load the truck",
            "Look at the wheel loader and the dump trucks: detailed like the rest of the fleet",
            "Dozers (try the mining dozer): blade down, drive into fresh ground: it cuts smooth, no bouncing or porpoising",
            "Multiplayer: one of you dozing and one digging: check how smooth it is (tell me the fps if it still lags)",
            "Grader: drop the blade to just touch and drive over rough ground: one pass leaves a smooth, even dirt road with a windrow at the side",
            "Drive a truck over a graded road: it stays smooth, no deep ruts",
            "Dig a deep pit and doze a cut: the walls are clean faces with layers in bands, no saw teeth, no long streaks, no grass at the corners",
            "Excavator (try the 50 t): dig a big hole: no dirt shooting straight up out of the ground, no lag",
            "Sit in an excavator or dozer on a slope: you lean with the machine; a friend in one leans too",
            "Dozer: lower the blade right down and drive into fresh topsoil: it cuts as deep as the blade's set and keeps cutting, the front doesn't ride up out of it",
            "Mining dozer: push a big heap a long way with the blade down: it keeps cutting, it doesn't climb up onto the ground",
            "Skid steer: tip the bucket down and drive into the dirt: it cuts in and fills; lift it out still tipped down and it pours",
            "Multiplayer: watch your friend push a lot of dirt with a dozer: no lag, and you still see their clods roll",
            "Excavator: shove the bucket straight down with the boom as hard as it goes: it barely goes in, the machine lifts, no dirt sprays and the bucket stays empty",
            "Excavator: open the bucket out and drag it in with the stick: it only takes a little dirt; curl it in as you pull and it fills",
            "Excavator: fill the bucket and lift it out curled: nothing falls out. Lift it out opened: it pours",
            "Excavator: dig a hole: the dirt it can't hold rolls off ahead of the bucket, not on top of it",
            "Look at a pit and heap up close: the facets are about half the size they were, and far off the ground fills in as you get close (no gaps or seams)",
            "Push dirt with a dozer and dig with an excavator: the frame rate feels as smooth as before (or better)",
            "Excavator: dig slowly and watch the bucket: the dirt goes away exactly where the bucket passes, no bucket inside drawn dirt",
            "Excavator: curl and pull in: the clumps come into the bucket at the teeth and tumble in, they don't pop up at the back",
            "Excavator: curl the bucket out and push a pile with its back, or swing it sideways through a pile: the pile gets shoved, the bucket stays empty",
            "Excavator: force the boom down into hard ground: the arm stops and the machine lifts up on the bucket instead of the bucket going in",
            "5 t excavator and dozers: lower the blade into the dirt standing still: the front lifts like on pavement",
            "Dump a full bucket on the ground and into a truck: the clods land as big as they were in the bucket",
            "Loose dirt has only a few clods on it now, not a carpet of them",
            "Excavator: dig a pit and dump the dirt beside it: the pit and the heap show lots of small facets and rounded sides, not big flat triangles",
            "Excavator: dig a ditch along a line: the walls go straight down and stay standing",
            "Dig a deep hole in sand: the walls cave in after a moment; in topsoil a 1 m hole keeps its walls",
            "Drive and look around the mine and town: the frame rate feels smoother, far ground fills in with detail as you get close (no gaps)",
            "Load one of your old saves: everything you dug is still there, as deep as before",
            "Dozer: lower the blade a little and push: the dirt rolls ahead as clods, piles up and spills off the ends, smooth with no jumps",
            "Dozer: push a long way with the blade below the ground: the machine stays level instead of nosing down into the cut",
            "Grader: angle the blade and drive: the clods roll along it and off the trailing end into a row",
            "Multiplayer: watch your friend push dirt: you see their clods roll in front of their blade, and the ground matches after",
            "Town: walk along East Ave and the north edge of town: no deep pit, the yards are flat and meet the hills",
            "Trench job on a slope: the blue box follows the ground, dig it and get paid",
            "Load an old save: what you dug in town is still there, at the same depth",
            "Multiplayer: your friend tilts a trailer deck: it moves on your screen right away. Then tilt one hitched to their truck yourself",
            "Main menu > Options: change the volumes, graphics and a key binding before loading in, then check they stuck in the game",
            "Excavator: hold stick in + curl + swing together (try the number pad 8/5/4/6 for boom and bucket): the panel shows all of them moving",
            "The game starts as Down and Dirty, and your saved worlds are still there",
            "Main menu: an excavator loads dump trucks behind the menu while a dozer pushes dirt to it",
            "Music: lofi plays in the menu and in the game; Esc > Audio: music volume, Next song, music while playing",
            "Excavator: the hydraulics whine as the arm moves, groan when you dig hard, and the dirt scrapes and pours",
            "Dozer: the blade scrapes as it pushes dirt, the tracks clank as you drive",
            "The menus all match: pause menu, settings tabs, key bindings, map, phone, dealer",
            "Multiplayer: the host quits, your friend goes back to the main menu with a message",
            "Multiplayer: join a friend while you're in your own world: you get their world",
            "Multiplayer: dump a truck into a pile: your friend sees the same pile, and can dig it",
            "Multiplayer: drive near each other with a bad connection: friends' machines move smoothly",
            "Pause single player (Esc): the engine and world sounds stop, the music keeps playing",
            "Accept a trench job: drive into town by the directions in the text (welcome board, street signs, orange beacon) and find the house",
            "Dig a trench to the light blue box: as wide and as deep as it shows, and get paid",
            "Get a few trench jobs: they're in different places on the lot, and different lengths, depths and widths",
            "Bump a mailbox or fence on a job: it falls over, they text you, and the pay is less",
            "Drive around town: the houses are all different (sizes, roofs, colours, fences, sheds, trees)",
            "Hitch a trailer with a skid steer to a dump truck, tow it to a job with gravel in the box, unhitch, dump and spread",
            "Look at the landscape supply bin: the text sits in its box",
            "Fill a loader bucket: the BUCKET FULL bar shows the whole message",
            "Two monitors: open the map / phone / menu, the mouse stays on the game's screen",
            "Multiplayer: accept a job with Share with crew, have your friend finish it, you both get half",
            "Multiplayer: take a job Just me, your friend's phone shows it's yours",
            "Open the map (M): scroll to zoom, drag it around, find your machines by their icons",
            "Look around: your arrow on the map and minimap turns with you",
            "Check the compass at the top isn't crowded any more",
            "Get a text: the new-message notice shows above the minimap",
            "Multiplayer: haul an excavator on a lowboy, your friend sees it move smoothly with its tracks still",
            "Drive the new road from the heavy yard over the hill to the town",
            "Wait for a text, open your phone (P), accept a job",
            "Buy gravel at the landscape supply: back the truck under the bin",
            "Dump the gravel on the driveway, unload a skid steer at the curb and spread it, then Finish on the phone",
            "Save in town, load: your jobs, texts and the dug yards come back",
            "Multiplayer: a friend sees the gravel and ditches you make in town",
            "Equipment dealer: click the categories, hover over the machines for their pictures and specs, buy one",
            "Upgrade the wash plant, save, load: the upgrades are still there",
            "Dig a deep hole with the 90 t, as deep as the arm reaches",
            "Controller: excavator, hold RT and steer the tracks with the left stick",
            "Dump truck: tailgate FULL OPEN, raise the box: the load rushes out in a few seconds",
            "Read the Dev Blog on the main menu",
        };

        static string Key(int i) => $"test.{TestListVersion}.{i}";
        public static bool Done(int i) => PlayerPrefs.GetInt(Key(i), 0) == 1;
        public static void SetDone(int i, bool done) { PlayerPrefs.SetInt(Key(i), done ? 1 : 0); PlayerPrefs.Save(); }
        public static int DoneCount() { int n = 0; for (int i = 0; i < TestList.Length; i++) if (Done(i)) n++; return n; }
        public static void ClearTicks() { for (int i = 0; i < TestList.Length; i++) PlayerPrefs.DeleteKey(Key(i)); PlayerPrefs.Save(); }
    }
}
