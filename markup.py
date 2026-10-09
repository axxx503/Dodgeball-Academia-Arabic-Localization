"""Parse nested game commands without treating their displayed arguments as IDs."""
from dataclasses import dataclass
from collections import Counter
import re

# IDs substituted by the game, not words to translate. Ruby carries two displayed
# parts and is currently preserved exactly pending bilingual layout integration.
# fx:flash|mystery / fx:shake|slap carry effect/sound identifiers,
# not displayed prose. Preserve their full bytes like other runtime references.
OPAQUE={'name','loc','rb','fx'}
WRAPPERS={'bold','small','slow','shake','wave','c','choice','size'}
@dataclass
class Node:
    text:str=''
    command:str=''
    children:list|None=None
    raw:str=''

def parse(text):
    def sequence(pos,closing=False):
        result=[];start=pos
        while pos<len(text):
            if text[pos]==']':
                if not closing:raise ValueError('unexpected closing bracket')
                if pos>start:result.append(Node(text=text[start:pos]))
                return result,pos+1
            if text[pos]!='[':pos+=1;continue
            if pos>start:result.append(Node(text=text[start:pos]))
            begin=pos;pos+=1;head_start=pos
            while pos<len(text) and text[pos] not in '|[]':pos+=1
            if pos==len(text) or text[pos]=='[':raise ValueError('unterminated command')
            head=text[head_start:pos]
            if text[pos]==']':
                pos+=1;result.append(Node(command=head,raw=text[begin:pos]))
            else:
                pos+=1
                if head.split(':')[0] in OPAQUE:
                    depth=1
                    while pos<len(text) and depth:
                        if text[pos]=='[':depth+=1
                        elif text[pos]==']':depth-=1
                        pos+=1
                    if depth:raise ValueError('unterminated reference')
                    result.append(Node(command=head,raw=text[begin:pos]))
                else:
                    children,pos=sequence(pos,True)
                    result.append(Node(command=head,children=children,raw=text[begin:pos]))
            start=pos
        if closing:raise ValueError('unterminated displayed argument')
        if pos>start:result.append(Node(text=text[start:pos]))
        return result,pos
    return sequence(0)[0]

def signature(nodes):
    fixed=[];wrappers=[];barriers=[];unknown=[]
    def visit(nodes):
        for n in nodes:
            if not n.command:continue
            if n.children is not None:
                # Each top-level pipe inside a choice separates a selectable
                # branch. Nested commands do not count as extra choices.
                branches=1+sum(c.text.count('|') for c in n.children) if n.command=='choice' else 0
                wrappers.append((n.command,branches))
                if n.command.split(':')[0] not in WRAPPERS:unknown.append(n.command)
                visit(n.children)
            else:
                fixed.append(n.raw)
                if n.command.split(':')[0] in {'x','d','e','p','w'}:barriers.append(n.raw)
    visit(nodes)
    return Counter(fixed),Counter(wrappers),barriers,unknown

def validate(source,translation):
    a=signature(parse(source));b=signature(parse(translation));errors=[]
    if a[0]!=b[0]:errors.append('control/reference tokens changed')
    if a[1]!=b[1]:errors.append('format wrapper commands changed')
    if a[2]!=b[2]:errors.append('interactive timing/panel order changed')
    if a[3] or b[3]:errors.append('unclassified displayed command')
    if protected_structure(parse(source))!=protected_structure(parse(translation)):
        errors.append('protected references moved between panels or choice branches')
    if '\t' in translation or '\n' in translation or '\r' in translation:errors.append('raw TSV line/tab delimiter introduced')
    return errors

def protected_structure(nodes):
    """References may move within Arabic prose, but not to a different panel/choice.

    [d] pauses and [n] line breaks can move during natural rephrasing. Panel and
    event barriers are fixed. Choice pipes count only at the current AST level.
    """
    panels=[[]];choices=[]
    def visit(seq):
        for node in seq:
            if not node.command:continue
            if node.children is not None:
                if node.command=='choice':
                    branches=[[]]
                    for child in node.children:
                        if child.command:branches[-1].append(child)
                        else:
                            parts=child.text.split('|')
                            branches[-1].append(Node(text=parts[0]))
                            for part in parts[1:]:branches.append([Node(text=part)])
                    choices.append(tuple(protected_structure(branch) for branch in branches))
                visit(node.children)
            elif node.command.split(':')[0] in {'x','e','p'}:
                panels[-1].append(node.raw);panels.append([])
            elif node.command not in {'d','n'}:
                panels[-1].append(node.raw)
    visit(nodes)
    return tuple(tuple(sorted(panel)) for panel in panels),tuple(choices)

def visible_text(nodes):
    return ''.join(n.text if not n.command else visible_text(n.children) if n.children is not None else '' for n in nodes)
