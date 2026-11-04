# MakeHuman head data

`head.npz` holds the head, neck and shoulders of the MakeHuman base mesh (hm08) and the shape targets
that `Docs/blender_basbilder.py` mixes to make each placeholder portrait (sex, build, ancestry, and the
head, forehead, nose, mouth, chin, cheek, eye, eyebrow, neck and ear sliders).

The MakeHuman base mesh and targets are released under **CC0 1.0** (public domain, free for any use,
no credit needed) by the MakeHuman team, see
https://github.com/makehumancommunity/makehuman (LICENSE.md, section C).

To rebuild it from a MakeHuman checkout:

    python3 Docs/makehuman/build_head_data.py <makehuman>/makehuman/data Docs/makehuman/head.npz
