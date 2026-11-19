(() => {
    const toggle = document.querySelector('.nav-toggle');
    const navigation = document.getElementById('main-navigation');
    if (toggle && navigation) {
        const mobile = window.matchMedia('(max-width: 860px)');
        const overlay = document.getElementById('sidebar-overlay');
        const close = () => {
            navigation.classList.remove('open');
            toggle.setAttribute('aria-expanded', 'false');
            if (overlay) overlay.style.display = 'none';
            document.body.style.overflow = '';
        };
        toggle.addEventListener('click', () => {
            const open = navigation.classList.toggle('open');
            toggle.setAttribute('aria-expanded', String(open));
            if (overlay) overlay.style.display = open ? 'block' : 'none';
            document.body.style.overflow = open ? 'hidden' : '';
            if (open) navigation.querySelector('nav a').focus();
        });
        if (overlay) overlay.addEventListener('click', close);
        document.addEventListener('keydown', event => {
            if (event.key === 'Escape' && navigation.classList.contains('open')) {
                close();
                toggle.focus();
            }
        });
        document.addEventListener('click', event => {
            if (!navigation.contains(event.target) && !toggle.contains(event.target)) close();
        });
        document.addEventListener('focusin', event => {
            if (mobile.matches && !navigation.contains(event.target) && event.target !== toggle) close();
        });
        mobile.addEventListener('change', close);
        navigation.querySelectorAll('nav a.active').forEach(link => link.setAttribute('aria-current', 'page'));
    }
    const errors = document.querySelector('.validation-summary-errors, [data-dispatch-error]');
    if (errors) errors.focus();
})();
