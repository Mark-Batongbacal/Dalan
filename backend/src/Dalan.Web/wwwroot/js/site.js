(() => {
    const toggle = document.querySelector('.nav-toggle');
    const navigation = document.getElementById('main-navigation');
    if (toggle && navigation) {
        const mobile = window.matchMedia('(max-width: 800px)');
        const close = () => {
            navigation.classList.remove('open');
            toggle.setAttribute('aria-expanded', 'false');
        };
        toggle.addEventListener('click', () => {
            const open = navigation.classList.toggle('open');
            toggle.setAttribute('aria-expanded', String(open));
            if (open) navigation.querySelector('nav a').focus();
        });
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
    const errors = document.querySelector('.validation-summary-errors');
    if (errors) errors.focus();
})();
