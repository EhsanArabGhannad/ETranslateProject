(() => {
    'use strict';
    const find = id => document.getElementById(id);
    const conditional = (selectId, containerId, value) => {
        const select = find(selectId), container = find(containerId);
        if (!select || !container) return;
        const update = () => { container.hidden = select.value !== value; };
        select.addEventListener('change', update); update();
    };
    conditional('notary', 'notary-options', 'Required');
    conditional('acceptance', 'other-options', 'Other');
    const template = find('template-select');
    if (template) {
        const update = () => { find('template-revision').value = template.selectedOptions[0]?.dataset.revision ?? ''; };
        template.addEventListener('change', update); update();
    }
})();
