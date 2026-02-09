/* ========================================
   AI Upscaler Website – JavaScript
======================================== */

document.addEventListener('DOMContentLoaded', () => {

    // --- Nav Scroll Effect ---
    const nav = document.getElementById('nav');
    let lastScroll = 0;
    window.addEventListener('scroll', () => {
        const y = window.scrollY;
        nav.classList.toggle('scrolled', y > 40);
        lastScroll = y;
    }, { passive: true });

    // --- Mobile Menu ---
    const mobileBtn = document.getElementById('mobileMenuBtn');
    const navLinks = document.querySelector('.nav-links');
    if (mobileBtn) {
        mobileBtn.addEventListener('click', () => {
            navLinks.classList.toggle('open');
            mobileBtn.classList.toggle('active');
        });
        // Close on link click
        navLinks.querySelectorAll('a').forEach(a => {
            a.addEventListener('click', () => {
                navLinks.classList.remove('open');
                mobileBtn.classList.remove('active');
            });
        });
    }

    // --- Scroll Reveal ---
    const reveals = document.querySelectorAll('[data-reveal]');
    const io = new IntersectionObserver((entries) => {
        entries.forEach((entry, i) => {
            if (entry.isIntersecting) {
                setTimeout(() => {
                    entry.target.classList.add('visible');
                }, i * 80);
                io.unobserve(entry.target);
            }
        });
    }, { threshold: 0.1, rootMargin: '0px 0px -40px 0px' });
    reveals.forEach(el => io.observe(el));

    // --- Code Tabs ---
    const tabs = document.querySelectorAll('.code-tab');
    tabs.forEach(tab => {
        tab.addEventListener('click', () => {
            const target = tab.dataset.tab;
            const parent = tab.closest('.code-block');
            // Deactivate all tabs
            parent.querySelectorAll('.code-tab').forEach(t => t.classList.remove('active'));
            parent.querySelectorAll('.code-content').forEach(c => c.style.display = 'none');
            // Activate clicked
            tab.classList.add('active');
            const codeEl = document.getElementById('code-' + target);
            if (codeEl) codeEl.style.display = 'block';
        });
    });

    // --- Copy Button ---
    document.querySelectorAll('.code-copy').forEach(btn => {
        btn.addEventListener('click', () => {
            const block = btn.closest('.code-block');
            const visibleCode = Array.from(block.querySelectorAll('.code-content'))
                .find(c => c.style.display !== 'none' || !c.style.display);
            const text = (visibleCode || block.querySelector('.code-content')).textContent;
            navigator.clipboard.writeText(text.trim()).then(() => {
                btn.classList.add('copied');
                btn.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="20 6 9 17 4 12"/></svg>';
                setTimeout(() => {
                    btn.classList.remove('copied');
                    btn.innerHTML = '<svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="9" y="9" width="13" height="13" rx="2"/><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"/></svg>';
                }, 2000);
            });
        });
    });

    // --- Smooth scroll for anchor links ---
    document.querySelectorAll('a[href^="#"]').forEach(a => {
        a.addEventListener('click', (e) => {
            const id = a.getAttribute('href');
            if (id === '#') return;
            const target = document.querySelector(id);
            if (target) {
                e.preventDefault();
                target.scrollIntoView({ behavior: 'smooth', block: 'start' });
            }
        });
    });

    // --- Counter Animation for Stats ---
    const statValues = document.querySelectorAll('.stat-value');
    const statObserver = new IntersectionObserver((entries) => {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('animated');
                statObserver.unobserve(entry.target);
            }
        });
    }, { threshold: 0.5 });
    statValues.forEach(el => statObserver.observe(el));

});
