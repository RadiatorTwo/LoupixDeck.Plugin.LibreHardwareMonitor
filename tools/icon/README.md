# Plugin icon generator

`make_icon.py` draws the plugin icon: a processor package seen from above, with a brushed metal
heat spreader and gold markings, on a deep green tile. It nods to the LibreHardwareMonitor icon
without copying it: the subject is redrawn from scratch in the soft, rounded style and shading of
the other plugin icons, with its own layout of markings. No file from LibreHardwareMonitor is
used. The deep green tile and the package motif keep it apart from the HWiNFO (chip with pins,
night blue) and LinuxHwInfo (terminal, graphite) icons.

```bash
pip install pillow numpy
python tools/icon/make_icon.py tools/icon/out
cp tools/icon/out/icon_256.png icon.png
```

`BG_HUE` at the top of the script sets the background tint.

The script writes `icon_{256,128,64,32,16}.png` into the given folder; only the 256 px file is
used, as `icon.png` in the repo root. The `out/` folder is not committed.
