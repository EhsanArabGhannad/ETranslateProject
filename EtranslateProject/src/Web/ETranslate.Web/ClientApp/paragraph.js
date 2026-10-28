export const advancedParagraphFields=Object.freeze(['spaceBeforePt','spaceAfterPt','firstLineIndentMm']);
export const paragraphFields=Object.freeze(['textAlign','lineHeight','indentLevel',...advancedParagraphFields]);
export const paragraphCommands=Object.freeze(Object.fromEntries(paragraphFields.map(field=>[field,field])));
export const paragraphActions=Object.freeze(['increaseIndent','decreaseIndent','clearParagraphStyle']);
export function validParagraphValue(field,value,version=3){
    if(!paragraphFields.includes(field)||advancedParagraphFields.includes(field)&&version<5)return false;
    if(value===null)return true;
    if(field==='textAlign')return ['left','center','right','justify'].includes(value);
    if(field==='lineHeight')return [1,1.15,1.5,1.65,2,2.5,3].includes(value);
    if(['spaceBeforePt','spaceAfterPt'].includes(field))return Number.isInteger(value)&&value>=0&&value<=72;
    if(field==='firstLineIndentMm')return Number.isFinite(value)&&value>=0&&value<=40&&Number.isInteger(value*2);
    return Number.isInteger(value)&&value>=0&&value<=8;
}
export function applyParagraph(element,attrs){
    for(const field of paragraphFields){const value=attrs?.[field];if(value==null)continue;
        if(!validParagraphValue(field,value,5))throw new Error('Unsupported paragraph setting');
        if(field==='textAlign')element.style.textAlign=value;
        else if(field==='lineHeight'){element.style.lineHeight=String(value);element.style.minHeight=`${value}em`;}
        else if(field==='indentLevel')element.style.marginInlineStart=`${value*5}mm`;
        else if(field==='spaceBeforePt')element.style.marginTop=`${value}pt`;
        else if(field==='spaceAfterPt')element.style.marginBottom=`${value}pt`;
        else element.style.textIndent=`${value}mm`;
    }
}
