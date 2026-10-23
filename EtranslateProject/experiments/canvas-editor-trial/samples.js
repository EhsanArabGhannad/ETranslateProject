export const rtlSentence = 'سلام دنیا';
export const mixedSentence = 'ترجمه رسمی - İstanbul 2026 / شماره ۱۲۳';
export const turkishSentence = 'İstanbul, ı, İ, ş, ğ, ö, ü - Türkçe çeviri deneyi.';
const p = (value, extra={}) => ({value:value+'\n', ...extra});
const cell = value => ({colspan:1,rowspan:1,value:[{value}]});
export function sample(id) {
    const header=[{value:'SENTETİK DENEY - RESMİ BELGE DEĞİLDİR',size:11}];
    const footer=[{value:'LOCAL TEST ONLY',size:11}];
    const data=main=>({header,main,footer});
    switch(id) {
        case 'rtl': return data([p('RTL SHAPING / CURSOR TEST',{bold:true}),p(rtlSentence,{size:28,rowFlex:'right'}),p(mixedSentence,{size:22,rowFlex:'right'}),p(turkishSentence,{size:20})]);
        case 'text': return data(Array.from({length:48},(_,i)=>p(`MARK_${String(i+1).padStart(3,'0')} `+(i%2?'این متن ساختگی برای بررسی صفحه‌بندی و جابه‌جایی نوشته است. هیچ اطلاعات شخصی ندارد.':turkishSentence+' Sayfalama metni değiştirmemelidir.'),{rowFlex:i%2?'right':'left'})));
        case 'long-table': return data([{type:'table',value:'',colgroup:[{width:130},{width:510}],trList:[{height:32,pagingRepeat:true,tdList:[cell('Sıra'),cell('Sentetik açıklama')]},...Array.from({length:46},(_,i)=>({height:32,tdList:[cell(`ROW_${String(i+1).padStart(3,'0')}`),cell('İstanbul / جدول ساختگی. '.repeat(4))]}))]},p('TABLE_END_MARKER')]);
        case 'merged': return data([{type:'table',value:'',colgroup:[{width:320},{width:320}],trList:[{height:42,tdList:[{...cell('ROWSPAN_SAMPLE'),rowspan:2},cell('A')]},{height:42,tdList:[cell('B')]}]},p('MERGED_END_MARKER')]);
        case 'tall-cell': return data([{type:'table',value:'',colgroup:[{width:640}],trList:[{height:32,tdList:[cell('TALL_START '+ 'Uzun hücre / خانه بلند. '.repeat(260)+' TALL_END')]}]},p('AFTER_TALL')]);
        case 'long-paragraph': return data([p('LONG_START '+'İstanbul متن بلند برای صفحه‌بندی. '.repeat(260)+' LONG_END')]);
        default: throw new Error('Unknown synthetic sample');
    }
}
export function flat(list) {
    return (list??[]).map(e=>e.type==='table'?e.trList.flatMap(r=>r.tdList.map(c=>flat(c.value))).join(''):e.valueList?flat(e.valueList):e.value??'').join('');
}
// U+200B is this engine's paragraph sentinel; preserve Persian ZWNJ (U+200C).
export const canonicalText = value => value.replace(/[\u200b\r\n]/g,'');
export const htmlFixture = '<h2 style="font-family:Arial;font-size:18pt;color:#b00020;text-align:center">WORD_STYLE_SAMPLE</h2><p style="font-family:Arial;font-size:16pt;background-color:#ffff00;line-height:2">İstanbul <strong>Bold</strong> <em>Italic</em> متن ساختگی</p>';

