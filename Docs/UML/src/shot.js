const { chromium } = require(process.env.PW);
(async () => {
  const [,, svg, png, scale, clip] = process.argv;
  const b = await chromium.launch();
  const p = await b.newPage({ deviceScaleFactor: parseFloat(scale || '1') });
  await p.goto('file://' + svg);
  const el = await p.$('svg');
  if (clip) {
    const [x, y, w, h] = clip.split(',').map(Number);
    await p.setViewportSize({ width: 3600, height: 2600 });
    await p.screenshot({ path: png, clip: { x, y, width: w, height: h } });
  } else {
    await el.screenshot({ path: png });
  }
  await b.close();
})();
