/* League Team Builder: look picker (step C1). With JavaScript off the <select> has an "Apply" button that posts normally.
   With it on, picking a look applies it at once (data-look + the look's fonts), then saves it in the background, so a page the
   user is working on (the team builder, for example) is not reloaded and loses nothing. If the save fails the form is posted normally. */
(function () {
  var form = document.querySelector('[data-lh-look-form]');
  if (!form) return;
  var select = form.querySelector('select');
  var apply = form.querySelector('[data-lh-look-apply]');
  var fonts = document.getElementById('lh-fonts');
  if (!select) return;
  if (apply) apply.hidden = true;

  select.addEventListener('change', function () {
    var option = select.options[select.selectedIndex];
    document.documentElement.setAttribute('data-look', select.value);
    if (fonts && option && option.getAttribute('data-fonts')) fonts.setAttribute('href', option.getAttribute('data-fonts'));
    fetch(form.action, { method: 'POST', body: new FormData(form), credentials: 'same-origin', headers: { 'X-LH-Ajax': '1' } })
      .then(function (r) { if (!r.ok) form.submit(); })
      .catch(function () { form.submit(); });
  });
})();
