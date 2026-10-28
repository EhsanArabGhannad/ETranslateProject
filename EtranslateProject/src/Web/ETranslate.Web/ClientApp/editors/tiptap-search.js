import {Plugin,PluginKey,TextSelection} from '@tiptap/pm/state';
import {Decoration,DecorationSet} from '@tiptap/pm/view';
import {closeHistory} from '@tiptap/pm/history';
import {literalMatches,validSearch,validReplacement,bodyTextBlocks} from '../document-text-tools.js';
import {isRichDocument} from '../document-schema.js';

// All positions and decorations stay inside the provider. Nothing is written to document JSON.
export function createTiptapSearch(){
    const key=new PluginKey('etranslateLiteralSearch');
    const empty={options:{query:'',caseSensitive:false,wholeWord:false},matches:[],truncated:false,index:-1,decorations:DecorationSet.empty};
    function collect(doc){const segments=[];doc.descendants((node,pos)=>{
        if(!node.isTextblock)return;
        let text='',start=pos+1;node.forEach((child,offset)=>{
            if(child.isText)text+=child.text;
            else {if(text)segments.push({text,from:start});text='';start=pos+1+offset+child.nodeSize;}
        });if(text)segments.push({text,from:start});return false;
    });return segments;}
    function decorate(doc,state){state.decorations=DecorationSet.create(doc,state.matches.map((match,index)=>Decoration.inline(match.from,match.to,{class:index===state.index?'editor-search-match editor-search-current':'editor-search-match'})));return state;}
    const plugin=new Plugin({key,state:{init:()=>empty,apply(tr,previous,oldState,newState){
        const meta=tr.getMeta(key);
        if(meta?.options||tr.docChanged){
            const options=meta?.options??previous.options;
            const result=literalMatches(options.query?collect(tr.doc):[],options);
            const anchor=meta?.options?newState.selection.from:tr.mapping.map(previous.matches[previous.index]?.from??newState.selection.from,1);
            let index=result.matches.findIndex(match=>match.from>=anchor);if(index<0&&result.matches.length)index=0;
            return decorate(tr.doc,{options,...result,index});
        }
        if(meta?.index!==undefined)return decorate(tr.doc,{...previous,index:meta.index});
        if(tr.selectionSet){const index=previous.matches.findIndex(match=>match.from===newState.selection.from&&match.to===newState.selection.to);
            if(index>=0&&index!==previous.index)return decorate(tr.doc,{...previous,index});}
        return previous;
    }},props:{decorations:state=>key.getState(state).decorations}});
    return {plugin,
        setSearch(editor,options){if(!validSearch(options))return false;editor.view.dispatch(editor.state.tr.setMeta(key,{options:{...options}}));return true;},
        getState(editor){const state=key.getState(editor.state);return {supported:true,total:state.matches.length,index:state.index+1,truncated:state.truncated};},
        find(editor,direction){const state=key.getState(editor.state);if(!state.matches.length)return false;
            const current=state.matches[state.index],selected=editor.state.selection.from===current.from&&editor.state.selection.to===current.to;
            const index=selected||direction<0?(state.index+direction+state.matches.length)%state.matches.length:state.index;
            const match=state.matches[index];editor.view.dom.focus({preventScroll:true});editor.view.focus();editor.view.dispatch(editor.state.tr.setSelection(TextSelection.create(editor.state.doc,match.from,match.to)).setMeta(key,{index}).scrollIntoView());
            // ProseMirror does not own an unfocused readonly DOM selection. Expose the same
            // range for navigation/copy without making the document editable.
            if(!editor.isEditable){const document=editor.view.dom.ownerDocument,range=document.createRange(),from=editor.view.domAtPos(match.from),to=editor.view.domAtPos(match.to);
                range.setStart(from.node,from.offset);range.setEnd(to.node,to.offset);const selection=document.getSelection();selection.removeAllRanges();selection.addRange(range);}
            return true;},
        replace(editor,value,all){if(!editor.isEditable||!validReplacement(value))return false;const state=key.getState(editor.state);
            if(!state.matches.length||all&&state.truncated)return false;
            const matches=all?state.matches:[state.matches[state.index]],tr=editor.state.tr;
            for(const match of [...matches].reverse()){
                if(tr.doc.textBetween(match.from,match.to)===value)continue;
                const marks=tr.doc.resolve(match.from).nodeAfter?.marks??[];
                if(value)tr.replaceWith(match.from,match.to,editor.schema.text(value,marks));else tr.delete(match.from,match.to);
            }
            if(!tr.docChanged)return false;
            const document=tr.doc.toJSON();
            if(!isRichDocument(document,5)||JSON.stringify(document).length>2000000||bodyTextBlocks(document).join('\n').length>1000000)return false;
            closeHistory(tr);editor.view.dispatch(tr);editor.view.dispatch(closeHistory(editor.state.tr));return true;
        }
    };
}
