import ui from '../Resources/interface.tr.json' with {type:'json'};
import {formatUi} from './ui-format.js';
import {documentStatistics} from './document-text-tools.js';

export function startEditorTextTools(workspace,session,source){
    const find=id=>document.getElementById(id),panel=find('editor-search'),statistics=find('editor-statistics');
    const cleanups=[];let timer;
    const number=value=>value.toLocaleString('tr-TR');
    function counts(json){if(statistics){const count=documentStatistics(JSON.parse(json));statistics.textContent=formatUi(ui.EditorDocumentCounts,number(count.words),number(count.characters),number(count.withoutSpaces));}}
    counts(source);
    cleanups.push(session.onDocumentChange(counts));
    if(!panel)return ()=>{for(const cleanup of cleanups)cleanup();};
    const query=find('editor-search-query'),replacement=find('editor-search-replacement'),matchCase=find('editor-search-case'),wholeWord=find('editor-search-word');
    const previous=find('editor-search-prev'),next=find('editor-search-next'),replace=find('editor-search-replace'),all=find('editor-search-all'),clear=find('editor-search-clear');
    const status=find('editor-search-status'),feedback=find('editor-search-feedback');
    function state(){const value=session.getSearchState();
        query.disabled=matchCase.disabled=wholeWord.disabled=!value.supported;
        previous.disabled=next.disabled=!value.supported||!value.total;
        replace.disabled=!value.canReplace||workspace.dataset.editable!=='true';
        // Replacement can be prepared before a match exists, but only in an editable session.
        replacement.disabled=!value.supported||!value.editable||workspace.dataset.editable!=='true';
        all.disabled=replace.disabled||value.truncated;clear.disabled=!value.supported||!query.value;
        status.textContent=!value.supported?ui.EditorSearchUnavailable:query.value?formatUi(ui.EditorSearchResults,number(value.index),number(value.total)+(value.truncated?'+':''))+(value.truncated?' '+ui.EditorSearchLimited:''):ui.EditorSearchPrompt;
    }
    const on=(element,event,handler)=>{element.addEventListener(event,handler);cleanups.push(()=>element.removeEventListener(event,handler));};
    function search(){clearTimeout(timer);timer=undefined;feedback.textContent='';if(!session.setSearch({query:query.value,caseSensitive:matchCase.checked,wholeWord:wholeWord.checked})){
        session.setSearch({query:'',caseSensitive:false,wholeWord:false});feedback.textContent=ui.EditorSearchInvalid;
    }state();}
    on(query,'input',event=>{if(!event.isComposing){clearTimeout(timer);timer=setTimeout(search,120);}});on(query,'compositionend',search);
    on(matchCase,'change',search);on(wholeWord,'change',search);
    // Keep current match across navigation; only flush a pending query edit.
    const move=direction=>{if(timer){clearTimeout(timer);timer=undefined;search();}session.findMatch(direction);state();};
    on(previous,'click',()=>move(-1));on(next,'click',()=>move(1));
    function change(every){if(timer){clearTimeout(timer);timer=undefined;search();}
        if(workspace.dataset.editable!=='true')return;
        const result=session.replaceSearch(replacement.value,every);feedback.textContent=result?ui.EditorReplacementDone:ui.EditorReplacementSkipped;state();}
    on(replace,'click',()=>change(false));on(all,'click',()=>change(true));
    on(clear,'click',()=>{query.value='';search();});
    for(const element of [query,replacement])on(element,'keydown',event=>{if(event.key==='Enter'&&!event.isComposing){event.preventDefault();if(element===replacement)change(false);else move(event.shiftKey?-1:1);}if(event.key==='Escape'){event.preventDefault();event.stopPropagation();panel.open=false;find('rich-text').querySelector('[role=textbox]')?.focus();}});
    on(workspace,'keydown',event=>{if((event.ctrlKey||event.metaKey)&&!event.altKey&&['f','h'].includes(event.key.toLowerCase())&&event.target.closest('#rich-region')){event.preventDefault();panel.open=true;query.focus();query.select();}});
    cleanups.push(session.onStateChange(state));state();
    return ()=>{clearTimeout(timer);for(const cleanup of cleanups)cleanup();};
}
