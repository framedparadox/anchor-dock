/* Cookie notice. This site sets no cookies and runs no analytics or ads; the only things kept in
   the visitor's browser are the light/dark choice and the fact that this notice was dismissed,
   both in localStorage and never sent anywhere. The notice says so, once, and stays dismissed. */
(function () {
  'use strict';
  var KEY = 'appsite-cookie-notice';
  try { if (localStorage.getItem(KEY) === 'dismissed') return; } catch (e) { /* storage blocked: show it */ }

  function mount() {
    if (document.querySelector('.cookie')) return;
    var box = document.createElement('section');
    box.className = 'cookie';
    box.setAttribute('role', 'region');
    box.setAttribute('aria-label', 'Cookie notice');
    box.innerHTML =
      '<p class="cookie__head">' +
        '<svg viewBox="0 0 24 24" aria-hidden="true"><circle cx="12" cy="12" r="9" fill="#f5b400"/>' +
        '<circle cx="9" cy="9.5" r="1.4" fill="#7a4a00"/><circle cx="14.5" cy="8" r="1.1" fill="#7a4a00"/>' +
        '<circle cx="15" cy="14" r="1.5" fill="#7a4a00"/><circle cx="9.5" cy="15.5" r="1.1" fill="#7a4a00"/></svg>' +
        'No tracking cookies here</p>' +
      '<p>This site uses no cookies, analytics or ads. Your browser only remembers your light or dark ' +
        'theme choice and that you have seen this note.</p>' +
      '<div class="cookie__actions">' +
        '<button type="button" class="btn btn--small">Got it</button>' +
        '<a href="/privacy#website">Privacy policy</a>' +
      '</div>';
    box.querySelector('button').addEventListener('click', function () {
      try { localStorage.setItem(KEY, 'dismissed'); } catch (e) {}
      box.classList.remove('is-shown');
      setTimeout(function () { box.remove(); }, 450);
    });
    document.body.appendChild(box);
    requestAnimationFrame(function () { requestAnimationFrame(function () { box.classList.add('is-shown'); }); });
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', mount);
  else mount();
})();
