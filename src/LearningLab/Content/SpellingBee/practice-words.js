// Learning Lab's own practice lists: everyday words picked for Learning Lab, from easy to hard ("a|b" = both
// spellings accepted, the first is the main one; "_" = a space). A school's own study list is added on each device
// with Spelling Bee's "Add a word list" (it is never bundled here).
const LISTS = [
 {id:"easy", name:"Easy Words", emoji:"🌱", note:"A good place to start", words:`cabin carve earth half fear apple happy kitten river garden pencil window yellow basket rabbit summer button jacket candle ladder little dinner pocket market sunny wagon`},
 {id:"medium", name:"Medium Words", emoji:"🐝", note:"A bit longer", words:`thunder blanket squirrel morning picture sandwich bicycle chicken mountain whisper turtle kitchen pumpkin feather library breakfast elephant umbrella balloon puzzle dolphin rainbow`},
 {id:"hard", name:"Hard Words", emoji:"🦉", note:"Tricky letters", words:`necessary beautiful February knowledge rhythm scissors island answer believe calendar Wednesday separate neighbor|neighbour cough enough laughter tongue guitar castle muscle science`},
 {id:"harder", name:"Harder Words", emoji:"🚀", note:"Long words", words:`fertilizer|fertiliser temperature environment encyclopedia restaurant thermometer vegetable conscience exaggerate accommodate millennium mischievous parliament silhouette pneumonia questionnaire occasionally embarrass definitely recommend`},
 {id:"hardest", name:"Hardest Words", emoji:"🏆", note:"From other languages", words:`a_cappella Addis_Ababa éclair bouquet chauffeur hors_d'oeuvre kindergarten piñata|pinata tsunami rendezvous croissant ballet souvenir debut|début fiancé|fiance mosquito quesadilla tortilla pizzeria spaghetti`},
];
