import { useEffect } from 'react';

// Covers existing modal implementations without changing their business flow.
export function useModalScrollLock() {
  useEffect(() => {
    let locked = false;
    let previousOverflow = '';
    const update = () => {
      const hasModal = !!document.querySelector('.modal');
      if (hasModal && !locked) {
        previousOverflow = document.body.style.overflow;
        document.body.style.overflow = 'hidden';
        locked = true;
      } else if (!hasModal && locked) {
        document.body.style.overflow = previousOverflow;
        locked = false;
      }
    };
    const observer = new MutationObserver(update);
    observer.observe(document.getElementById('root')!, { childList: true, subtree: true });
    update();
    return () => { observer.disconnect(); if (locked) document.body.style.overflow = previousOverflow; };
  }, []);
}
