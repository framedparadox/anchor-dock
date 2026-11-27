/* Feedback form: builds a readable, pre-filled email and hands it to the visitor's mail app.
   Nothing is sent to a server. Without JavaScript the form still falls back to a mailto: GET. */
(function () {
  'use strict';
  var TO = 'hello@anchordock-app.com';
  var form = document.getElementById('feedback-form');
  if (!form) return;
  form.addEventListener('submit', function (e) {
    e.preventDefault();
    if (!form.reportValidity()) return;
    var v = function (n) { return (form.elements[n].value || '').trim(); };
    var lines = [v('message'), '', '---'];
    if (v('name')) lines.push('Name: ' + v('name'));
    if (v('email')) lines.push('Reply to: ' + v('email'));
    lines.push('Sent from the Anchor website feedback form');
    var url = 'mailto:' + TO +
      '?subject=' + encodeURIComponent('Anchor ' + v('type').toLowerCase()) +
      '&body=' + encodeURIComponent(lines.join('\n'));
    window.location.href = url;
  });
})();
