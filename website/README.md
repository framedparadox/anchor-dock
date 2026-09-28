# Anchor website

The product site for Anchor: static HTML, CSS and JavaScript with no build step and no dependencies.

```
website/
├── index.html          Product page
├── privacy.html        Privacy policy (mirrors docs/privacy-policy.md)
├── 404.html            Not-found page for GitHub Pages
├── robots.txt, sitemap.xml
└── assets/
    ├── css/site.css
    ├── js/site.js
    └── img/            WebP screenshots, icons and the social preview (og.jpg)
```

## Preview locally

```powershell
cd website
python -m http.server 8000
```

Then open <http://localhost:8000>. Opening `index.html` straight from disk also works, apart from
the 404 page, which uses absolute `/anchor-dock/` paths.

## Deploy

`.github/workflows/pages.yml` publishes this folder to GitHub Pages on every push to `main` that
touches `website/`. Enable it once under **Settings ▸ Pages ▸ Source: GitHub Actions**. The site
is then served at <https://framedparadox.github.io/anchor-dock/>. If you move it to a custom
domain, update the canonical/Open Graph URLs in the HTML files, `sitemap.xml`, `robots.txt`, and
the `/anchor-dock/` paths in `404.html`.

## Keeping it in sync

- **Version and specs**: the version appears in `index.html` (hero line, Tech Specs, JSON-LD).
- **Screenshots**: the images in `assets/img/` are WebP copies of `img/*.png`, resized to at most
  1600 px wide. Regenerate them when the app's UI changes.
- **Privacy policy**: `privacy.html` is a copy of `docs/privacy-policy.md`. Update both together.
