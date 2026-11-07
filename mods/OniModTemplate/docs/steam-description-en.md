Welcome to OniModTemplate! A blank Oxygen Not Included mod you can actually build.
With this project, [b]a new mod starts from ModInfo, a standard folder layout, and the onim build path.[/b]

Patches go in Patches/. Shared logic goes in Core/. Overlays go in UI/. Options go in Config/. The root stays limited to the entry point and the project files.

[h2]Current Stage: v0.1.7[/h2]
[i]This is a developer scaffold, not a colony mod. Enabling the template in a save does not add gameplay.[/i]
[i]Copy it into a new mod before you publish. Do not ship the template static ID as a finished Workshop item.[/i]

[h3]Supported DLC:[/h3]
[img]https://raw.githubusercontent.com/Sgt-Imalas/Sgt_Imalas-Oni-Mods/refs/heads/master/Compat_All.png[/img]

[h3]MODDING API[/h3]
The template targets the game's mod API 2 and declares support for all content.
Build and install from the repository root:

onim build -m OniModTemplate
onim dev -m OniModTemplate

PLib remains optional. Add it when the new mod needs options, translations, or shared UI. Consumers of Jlib should reference the shared provider and must not merge a private copy.

[h3]What you get[/h3]
A predictable layout for Harmony patches, a ModInfo entry point, and metadata that onim already knows how to pack.
There is no gameplay hook, no required library, and no Workshop page for this template.

[h3]Controls and Quality of Life[/h3]
The only controls are the ones you add later.

* Keep ModInfo.cs and the project files at the mod root.
* Put patches under Patches/ and reusable code under Core/.
* Run onim dev when you want the local debug install.
* Replace the title, static ID, and these description files before a first publication.

[h3]Built with onim[/h3]
This mod comes from [url=https://github.com/LIghtJUNction/OniMods]OniMods[/url]. onim is the toolchain in that repo. It takes an Oxygen Not Included mod straight through development, testing, and Workshop publishing.

onim dev tries it in game. onim build packs it. onim publish sends it to Steam.

[h3]Contributions[/h3]
Contributions are welcome!
If you have improvements, fixes, or new features, feel free to open a Pull Request.

Please make sure your changes are clear and well-documented where necessary.

You can find the [url=https://github.com/LIghtJUNction/OniMods/tree/main/mods/OniModTemplate]GitHub repository here[/url]

[h3]Disclaimer[/h3]
Please remember that this template moves with the repository and will receive layout updates.
Your feedback and suggestions are highly appreciated.
It has no published Workshop ID. A first public item would need the staged Legacy ZIP flow, not a directory upload.

[h3]Contact[/h3]
Questions go to the [url=https://github.com/LIghtJUNction/OniMods/issues]GitHub issue tracker[/url].

Have fun out there. This page is a blank duplicant form. You bring the germs.
[img]https://i.ibb.co/LXdJf9K9/I3P8A6f.gif[/img]
