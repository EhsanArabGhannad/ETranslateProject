export const scriptMarks=Object.freeze(['superscript','subscript']);
export function applyInlineScript(element,type){
    if(!scriptMarks.includes(type))throw new Error('Unsupported inline script');
    Object.assign(element.style,{fontSize:'.75em',lineHeight:'0',verticalAlign:'baseline',position:'relative',top:type==='superscript'?'-.5em':'.25em'});
}
// Wrapping order: ordinary marks, script, typography. Size/color and text decorations
// therefore inherit the same nesting as the provider, regardless of JSON array order.
export function orderedInlineMarks(marks){const rank=mark=>mark?.type==='typography'?2:scriptMarks.includes(mark?.type)?1:0;return [...marks].sort((a,b)=>rank(a)-rank(b));}
