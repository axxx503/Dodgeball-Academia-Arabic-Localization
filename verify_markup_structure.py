"""Guard panel/branch allocation: token counts alone cannot detect a swapped ID."""
from markup import validate
cases=[
 ('[fx:flash|mystery]Hello,[x]','[fx:flash|mystery]أهلًا،[x]',True),
 ('[fx:flash|mystery]Hello,[x]','[fx:flash|slap]أهلًا،[x]',False),
 ('[name|otto] goes first.[x]Then [name|mina].[x]',
  'يبدأ [name|otto].[x]ثم [name|mina].[x]',True),
 ('[name|otto] goes first.[x]Then [name|mina].[x]',
  'تبدأ [name|mina].[x]ثم [name|otto].[x]',False),
 ('[choice|[bold|Choose [name|otto]]|Choose [name|mina]]',
  '[choice|[bold|اختر [name|otto]]|اختر [name|mina]]',True),
 ('[choice|[bold|Choose [name|otto]]|Choose [name|mina]]',
  '[choice|[bold|اختر [name|mina]]|اختر [name|otto]]',False),
 ('Hello,[d] [name|otto]![n]Goodbye.[x]',
  'أهلًا [name|otto]،[d]![n]إلى اللقاء.[x]',True),
 ('[choice|[c|Accept]|[small|Decline]]',
  '[choice|[c|اقبل]|[small|ارفض]]',True),
 ('[choice|[c|Accept]|[small|Decline]]',
  '[choice|[c|اقبل]|[small|ارفض]|خيار زائد]',False),
]
for source,target,accepted in cases:
    actual=validate(source,target)
    assert (not actual)==accepted,(source,target,actual)
print(f'{len(cases)} panel/choice mutation fixtures passed')
