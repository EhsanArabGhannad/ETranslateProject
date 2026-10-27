export const searchLimit=2000;
const wellFormed=value=>!/[\uD800-\uDBFF](?![\uDC00-\uDFFF])|(?<![\uD800-\uDBFF])[\uDC00-\uDFFF]/.test(value);
export function validSearch(options){return options!==null&&typeof options==='object'&&!Array.isArray(options)&&Object.keys(options).every(key=>['query','caseSensitive','wholeWord'].includes(key))&&typeof options.query==='string'&&options.query.length<=200&&wellFormed(options.query)&&!/[\r\n\u2028\u2029]/u.test(options.query)&&typeof options.caseSensitive==='boolean'&&typeof options.wholeWord==='boolean';}
export function validReplacement(value){return typeof value==='string'&&value.length<=2000&&wellFormed(value)&&!/[\r\n\u2028\u2029]/u.test(value);}
const words=new Intl.Segmenter('tr',{granularity:'word'}),graphemes=new Intl.Segmenter('tr',{granularity:'grapheme'});
export function bodyTextBlocks(document){
    const blocks=[];
    function visit(node){if(['paragraph','heading'].includes(node?.type))blocks.push((node.content??[]).map(child=>child.type==='text'?child.text:child.type==='hardBreak'?'\n':'').join(''));
        else for(const child of node?.content??[])visit(child);}
    visit(document);return blocks;
}
export function documentStatistics(document){
    const text=bodyTextBlocks(document).join('\n');let wordCount=0,characters=0,withoutSpaces=0;
    for(const item of words.segment(text))if(item.isWordLike)wordCount++;
    for(const {segment} of graphemes.segment(text)){if(/^[\r\n\u2028\u2029]+$/u.test(segment))continue;characters++;if(!/^\s+$/u.test(segment))withoutSpaces++;}
    return {words:wordCount,characters,withoutSpaces};
}
function fold(text,sensitive){
    let value='';const boundaries=new Map([[0,0]]);let original=0;
    for(const point of text){value+=sensitive?point:point.toLocaleLowerCase('tr');original+=point.length;boundaries.set(value.length,original);}
    return {value,boundaries};
}
// Text segments are separate at hard breaks, paragraphs and cells; marks do not create boundaries.
export function literalMatches(segments,options){
    if(!validSearch(options))throw new Error('Invalid literal search');
    if(!options.query)return {matches:[],truncated:false};
    const needle=fold(options.query,options.caseSensitive).value,matches=[];
    for(const segment of segments){const folded=fold(segment.text,options.caseSensitive);
        const starts=new Set(),ends=new Set();if(options.wholeWord)for(const item of words.segment(segment.text))if(item.isWordLike){starts.add(item.index);ends.add(item.index+item.segment.length);}
        let offset=0;
        while(offset<=folded.value.length-needle.length){const index=folded.value.indexOf(needle,offset);if(index<0)break;const end=index+needle.length;
            const from=folded.boundaries.get(index),to=folded.boundaries.get(end);
            if(from!==undefined&&to!==undefined&&(!options.wholeWord||starts.has(from)&&ends.has(to))){
                if(matches.length===searchLimit)return {matches,truncated:true};
                matches.push({from:segment.from+from,to:segment.from+to});offset=end;
            }else offset=index+1;
        }
    }return {matches,truncated:false};
}
