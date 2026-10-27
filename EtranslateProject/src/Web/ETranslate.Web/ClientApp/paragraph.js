export const paragraphFields=Object.freeze(['textAlign','lineHeight','indentLevel']);
export const paragraphCommands=Object.freeze({textAlign:'textAlign',lineHeight:'lineHeight',indentLevel:'indentLevel'});
export const paragraphActions=Object.freeze(['increaseIndent','decreaseIndent','clearParagraphStyle']);
export function validParagraphValue(field,value){
    if(!paragraphFields.includes(field))return false;
    if(value===null)return true;
    if(field==='textAlign')return ['left','center','right','justify'].includes(value);
    if(field==='lineHeight')return [1,1.15,1.5,1.65,2,2.5,3].includes(value);
    return Number.isInteger(value)&&value>=0&&value<=8;
}
export function applyParagraph(element,attrs){
    for(const field of paragraphFields){const value=attrs?.[field];if(value==null)continue;
        if(!validParagraphValue(field,value))throw new Error('Unsupported paragraph setting');
        if(field==='textAlign')element.style.textAlign=value;
        else if(field==='lineHeight'){element.style.lineHeight=String(value);element.style.minHeight=`${value}em`;}
        else element.style.marginInlineStart=`${value*5}mm`;
    }
}
