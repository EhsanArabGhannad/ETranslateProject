import ui from '../Resources/interface.tr.json' with { type: 'json' };
import { formatUi } from './ui-format.js';
import { footerPolicy } from './footer-policy.js';
import { geometry, pageAt } from './pagination-geometry.js';
import { measureRowPlan } from '../../../Shared/DocumentLayout/table-row-plan.js';
import { isRichDocument } from './document-schema.js';

export function supportedPaginationRegion(source, type) {
    if(source==null || source==='')return true;
    if(typeof source!=='string')return false;
    try {
        const value=JSON.parse(source);
        // Existing simple letterheads use a doc root. Match the server's legacy fallback
        // without granting template-only attributes to a doc-root body vocabulary.
        if(value.type==='doc')return isRichDocument(value);
        if(value.type!==type)return false;
        function normalize(node) {
            if(!node || typeof node!=='object' || Array.isArray(node))return node;
            const copy={...node};if(node.attrs){copy.attrs={...node.attrs};
                if(Object.hasOwn(copy.attrs,'textAlign')){if(!['paragraph','heading'].includes(node.type)||!['left','center','right'].includes(copy.attrs.textAlign))throw new Error();delete copy.attrs.textAlign;}
                if(node.type==='image')for(const [key,values] of [['align',['left','center','right']],['layout',['logo','banner']]])
                    if(Object.hasOwn(copy.attrs,key)){if(!values.includes(copy.attrs[key]))throw new Error();delete copy.attrs[key];}}
            if(Array.isArray(node.content))copy.content=node.content.map(normalize);return copy;
        }
        return isRichDocument({...normalize(value),type:'doc'});
    }catch{return false;}
}

// View-only controller. It never writes JSON, issues editor commands, remounts or unlocks a session.
export function startEditorPagination(workspace, session, schedule) {
    const find = id => document.getElementById(id), toggle = find('editor-pages-toggle'), nav = find('editor-pages-nav');
    const root = find('rich-text')?.querySelector('[role=textbox]'), paper = find('editor-paper'), stage = find('editor-paper-stage');
    const viewport = find('editor-viewport'), host = find('rich-text'), select = find('editor-page-index'), notice = find('editor-pagination-status');
    if (!toggle || !nav || !root || !session) return null;
    let enabled = false, disposed = false, loading = false, count = 1, active = 1, scale = 1, metrics, templates, painted = 0, follow = false;
    const sheets = document.createElement('div'); sheets.id = 'editor-page-sheets'; sheets.setAttribute('aria-hidden','true');
    const measure = document.createElement('div'); measure.id = 'editor-flow-measure'; measure.inert = true; measure.setAttribute('aria-hidden','true');
    workspace.append(measure);
    const saved = new Map();
    const description = find('editor-paper-description'), continuousDescription = description?.textContent;
    function remember(node) { saved.set(node,[...node.style].map(name=>[name,node.style.getPropertyValue(name),node.style.getPropertyPriority(name)])); }
    function restore() { for(const [node,properties] of saved){while(node.style.length)node.style.removeProperty(node.style.item(0));for(const [n,v,p] of properties)node.style.setProperty(n,v,p);}saved.clear(); }
    function stop(message = '') {
        enabled = false; root.classList.remove('document-flow'); paper.classList.remove('editor-page-world');
        sheets.remove(); measure.replaceChildren(); restore(); nav.hidden = true; painted = 0;
        delete viewport.dataset.pageCount; delete viewport.dataset.pageIndex; viewport.dataset.pagination = 'continuous';
        toggle.setAttribute('aria-pressed','false'); toggle.textContent = ui.EditorEnablePages; notice.textContent = message; schedule();
        if(description)description.textContent=continuousDescription;
    }
    function region(id, kind) {
        const source = find(id), node = source.cloneNode(true); node.removeAttribute('id'); node.hidden = false;
        node.className = `document-flow pagination-region pagination-${kind}`;
        // Preview renderer already allowlists this DOM; preserve only supported paragraph alignment.
        for(const item of [node,...node.querySelectorAll('*')]) {
            const align = item.style.textAlign; item.removeAttribute('id'); item.removeAttribute('style');
            if(['left','center','right'].includes(align))item.style.textAlign=align;
        }
        node.style.width = `${geometry(find('template-layout')?.value).contentWidth}px`;
        return node;
    }
    async function switchMode() {
        if(loading || disposed)return;
        if(enabled){stop();return;}
        loading = true; toggle.disabled = true; notice.textContent = ui.EditorPagesLoading;
        try {
            if(!geometry(find('template-layout')?.value))throw new Error();
            await Promise.all(['11pt "Noto Sans"','bold 11pt "Noto Sans"','italic 11pt "Noto Sans"','bold italic 11pt "Noto Sans"','11pt "Noto Sans Arabic"','bold 11pt "Noto Sans Arabic"',
                '11pt "Noto Serif"','bold 11pt "Noto Serif"','italic 11pt "Noto Serif"','bold italic 11pt "Noto Serif"'].map(font=>document.fonts.load(font)));
            if(disposed)return;
            if(root.querySelector('.missing-asset') || !supportedPaginationRegion(find('template-header')?.value,'header') ||
                !supportedPaginationRegion(find('template-footer')?.value,'footer'))throw new Error();
            templates = [region('preview-header','header'),region('preview-footer','footer')];
            for(const [i,id] of ['template-header','template-footer'].entries()) {
                const source=find(id)?.value;let images=0;
                const count=node=>{if(node?.type==='image')images++;for(const child of node?.content??[])count(child);};
                if(source)count(JSON.parse(source));
                if(images!==templates[i].querySelectorAll('img').length)throw new Error();
            }
            measure.replaceChildren(...templates);
            for(const image of [...templates.flatMap(node=>[...node.querySelectorAll('img')]),...root.querySelectorAll('img')])await image.decode();
            if(disposed)return;
            const sizes=templates.map(node=>node.textContent || node.querySelector('img') ? node.getBoundingClientRect().height*25.4/96 : 0);
            metrics=geometry(find('template-layout')?.value,...sizes); measure.replaceChildren();
            if(!metrics)throw new Error();
            for(const node of [root,paper,host])remember(node);
            enabled=true; follow=true; root.classList.add('document-flow'); paper.classList.add('editor-page-world'); paper.prepend(sheets);
            toggle.setAttribute('aria-pressed','true'); toggle.textContent=ui.EditorDisablePages; nav.hidden=false;
            if(description)description.textContent=formatUi(ui.EditorPagedDescription,metrics.pageSize,metrics.orientation==='Landscape'?ui.EditorLandscape:ui.EditorPortrait,metrics.width,metrics.height);
            notice.textContent=ui.EditorPagesNotice; schedule();
        } catch { stop(ui.EditorPagesUnavailable); }
        finally { loading=false;if(!disposed)toggle.disabled=false; }
    }
    function selectionPage() {
        const selection=getSelection();if(!selection?.focusNode || !root.contains(selection.focusNode))return null;
        const range=document.createRange();range.setStart(selection.focusNode,selection.focusOffset);range.collapse(true);
        const rect=[...range.getClientRects()].find(rect=>rect.height>0);
        return rect?pageAt((rect.left-root.getBoundingClientRect().left)/scale,metrics):null;
    }
    function navigation() {
        if(select.options.length!==count){select.replaceChildren();for(let i=1;i<=count;i++){const option=document.createElement('option');option.value=String(i);option.textContent=String(i);select.append(option);}}
        select.value=String(active);find('editor-page-prev').disabled=active<=1;find('editor-page-next').disabled=active>=count;
        find('editor-page-count').textContent=formatUi(ui.PageOf,active,count);
        viewport.dataset.pageCount=String(count);viewport.dataset.pageIndex=String(active);viewport.dataset.pagination='paged';
    }
    function jump(page) {
        if(!enabled || !Number.isInteger(page) || page<1 || page>count)return;
        active=page;viewport.scrollLeft=(page-1)*metrics.pitch*scale;viewport.scrollTop=0;navigation();
    }
    function followCaret(){if(enabled){const page=selectionPage();if(page && page!==active && page<=count)jump(page);}}
    function paint() {
        if(painted===count)return; sheets.replaceChildren();
        for(let i=0;i<count;i++) {
            const sheet=document.createElement('article');sheet.className='editor-page-sheet';
            Object.assign(sheet.style,{left:`${i*metrics.pitch}px`,width:`${metrics.pageWidth}px`,height:`${metrics.pageHeight}px`});
            const mark=document.createElement('div');mark.className='editor-page-draft';mark.textContent='DRAFT - UNSIGNED';sheet.append(mark);
            const watermark=find('template-watermark')?.value;
            if(watermark){const value=JSON.parse(watermark),custom=document.createElement('div');
                if(typeof value.text!=='string' || value.text.length>200 || Object.keys(value).some(key=>!['text','opacity','rotation'].includes(key)) ||
                    !Number.isFinite(value.opacity??.12) || (value.opacity??.12)<0 || (value.opacity??.12)>.3 || !Number.isFinite(value.rotation??-35) || Math.abs(value.rotation??-35)>90)throw new Error();
                custom.className='editor-page-watermark';custom.dir='auto';custom.textContent=value.text;custom.style.opacity=String(value.opacity??.12);custom.style.transform=`rotate(${value.rotation??-35}deg)`;sheet.append(custom);}
            const head=templates[0].cloneNode(true), foot=templates[1].cloneNode(true);
            Object.assign(head.style,{left:`${metrics.left}px`,top:`${metrics.margins.top*96/25.4}px`});
            Object.assign(foot.style,{left:`${metrics.left}px`,bottom:`${(metrics.margins.bottom+8)*96/25.4}px`});
            if(head.textContent || head.querySelector('img'))sheet.append(head);
            if((foot.textContent || foot.querySelector('img')) && (footerPolicy(find('template-layout')?.value)!=='LastPage' || i===count-1))sheet.append(foot);
            const number=document.createElement('div');number.className='editor-page-number';number.textContent=`DRAFT - UNSIGNED | ${formatUi(ui.PageOf,i+1,count)}`;sheet.append(number);
            sheets.append(sheet);
        }
        painted=count;
    }
    function render(value) {
        if(!enabled)return false;
        try {
            // Do not start another private-image request on every keystroke. Images are already
            // decoded; a read-only measurement box uses their actual unscaled dimensions.
            const currentScale=new DOMMatrixReadOnly(getComputedStyle(paper).transform).a || 1;
            function copy(node) {
                if(node instanceof HTMLImageElement){const box=document.createElement('span'),rect=node.getBoundingClientRect();
                    Object.assign(box.style,{display:'block',width:`${rect.width/currentScale}px`,height:`${rect.height/currentScale}px`,breakInside:'avoid'});return box;}
                const clone=node.cloneNode(false);for(const child of node.childNodes)clone.append(copy(child));return clone;
            }
            const clone=copy(root);clone.removeAttribute('contenteditable');clone.removeAttribute('role');clone.removeAttribute('tabindex');
            Object.assign(clone.style,{width:`${metrics.contentWidth}px`,height:'auto',columns:'auto'});measure.replaceChildren(clone);
            for(const table of clone.querySelectorAll('table')) {
                const rows=[...table.rows], first=rows[0];
                const header=first?.cells.length && [...first.cells].every(cell=>cell.tagName==='TH')?rows.shift().getBoundingClientRect().height:0;
                if(Math.max(header,...rows.map(row=>header+row.getBoundingClientRect().height))>metrics.contentHeight+.5)throw new Error();
            }
            const previousScale=scale,previousActive=active;
            const plan=measureRowPlan(clone,metrics);measure.replaceChildren();count=plan.pages;active=Math.min(active,count);scale=value;
            Object.assign(root.style,{width:`${metrics.contentWidth}px`,height:`${metrics.contentHeight}px`,columnCount:'1',columnWidth:`${metrics.contentWidth}px`,columnGap:`${metrics.columnGap}px`,columnFill:'auto'});
            Object.assign(host.style,{left:`${metrics.left}px`,top:`${metrics.top}px`});
            const width=count*metrics.pageWidth+(count-1)*28;
            Object.assign(paper.style,{padding:'0',width:`${width}px`,height:`${metrics.pageHeight}px`,minHeight:'0',transform:`scale(${scale})`});
            stage.style.width=`${width*scale}px`;stage.style.height=`${metrics.pageHeight*scale}px`;
            paint();navigation();if(previousScale!==scale || previousActive!==active)jump(active);
            if(follow){follow=false;followCaret();}return true;
        } catch {stop(ui.EditorPagesUnavailable);return false;}
    }
    const previous=()=>jump(active-1),next=()=>jump(active+1),chosen=()=>jump(Number(select.value));
    const keep=event=>event.preventDefault();const key=event=>{if(!event.isComposing && ['ArrowUp','ArrowDown','ArrowLeft','ArrowRight','Home','End','PageUp','PageDown'].includes(event.key))followCaret();};
    toggle.addEventListener('mousedown',keep);toggle.addEventListener('click',switchMode);
    find('editor-page-prev').addEventListener('mousedown',keep);find('editor-page-next').addEventListener('mousedown',keep);
    find('editor-page-prev').addEventListener('click',previous);find('editor-page-next').addEventListener('click',next);select.addEventListener('change',chosen);
    document.addEventListener('selectionchange',followCaret);root.addEventListener('keyup',key);
    const unsubscribe=session.onDocumentChange(()=>{follow=true;schedule();});
    return {render,destroy(){disposed=true;stop();measure.remove();unsubscribe();toggle.removeEventListener('mousedown',keep);toggle.removeEventListener('click',switchMode);
        find('editor-page-prev').removeEventListener('mousedown',keep);find('editor-page-next').removeEventListener('mousedown',keep);
        find('editor-page-prev').removeEventListener('click',previous);find('editor-page-next').removeEventListener('click',next);select.removeEventListener('change',chosen);
        document.removeEventListener('selectionchange',followCaret);root.removeEventListener('keyup',key);}};
}
