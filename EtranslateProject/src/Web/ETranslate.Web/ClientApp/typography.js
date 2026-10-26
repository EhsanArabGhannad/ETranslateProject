export const fontFamilies=Object.freeze({sans:"'Noto Sans','Noto Sans Arabic',sans-serif",serif:"'Noto Serif','Noto Sans Arabic',serif",arabic:"'Noto Sans Arabic','Noto Sans',sans-serif"});
export const typographyFields=Object.freeze(['fontFamily','fontSize','color','highlight']);
export const typographyCommands=Object.freeze({fontFamily:'fontFamily',fontSize:'fontSize',textColor:'color',highlight:'highlight'});
export function validTypographyValue(field,value){
    if(!typographyFields.includes(field))return false;
    if(value===null)return true;
    if(field==='fontFamily')return typeof value==='string'&&Object.hasOwn(fontFamilies,value);
    if(field==='fontSize')return Number.isInteger(value)&&value>=6&&value<=72;
    return typeof value==='string'&&value.length===7&&/^#[0-9a-f]{6}$/i.test(value);
}
export function validTypography(attrs){
    return attrs!==null&&typeof attrs==='object'&&!Array.isArray(attrs)&&Object.keys(attrs).length>0&&
        Object.entries(attrs).every(([field,value])=>validTypographyValue(field,value))&&Object.values(attrs).some(value=>value!==null);
}
export function applyTypography(element,attrs){
    if(!validTypography(attrs))return false;
    for(const field of typographyFields){const value=attrs[field];if(value==null)continue;
        element.style[{fontFamily:'fontFamily',fontSize:'fontSize',color:'color',highlight:'backgroundColor'}[field]]=field==='fontFamily'?fontFamilies[value]:field==='fontSize'?`${value}pt`:value;
    }return true;
}
