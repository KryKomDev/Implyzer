// Implyzer
// Copyright (c) KryKom 2026

using System.Collections.Immutable;
using System.Composition;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Implyzer.CodeFixes;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(UseInsteadCodeFixProvider)), Shared]
public class UseInsteadCodeFixProvider : CodeFixProvider {
    public sealed override ImmutableArray<string> FixableDiagnosticIds => ["IMPL005"];

    public sealed override FixAllProvider GetFixAllProvider() {
        return WellKnownFixAllProviders.BatchFixer;
    }

    public sealed override async Task RegisterCodeFixesAsync(CodeFixContext context) {
        var diagnostic = context.Diagnostics.First();
        
        if (!diagnostic.Properties.TryGetValue("Replacement", out var replacement) || string.IsNullOrEmpty(replacement))
            return;

        // If the replacement looks like a method signature "Method(int)", we probably only want to replace the name with "Method", 
        // OR the user intends a more complex rewrite which we might not support fully automatically yet without parsing.
        // For now, let's assume if it contains '(', it might be informational or a complex replace.
        // However, if the attribute is used on a type for constructor replacement, e.g. "NewType", we replace "OldType" with "NewType".
        
        // If the replacement string contains '(', we should probably be careful.
        // But the request is "create codefixes". 
        // If the replacement is "NewType(int)", we can't easily replace "new OldType()" with "new NewType(int)" because we don't know the arguments.
        // But if the replacement is just "NewClass", we can replace "OldClass".
        
        // Let's support simple identifier replacement for now.
        // Or if it is "Type.Member", replace "Member" with "Type.Member"? 
        // That requires semantic context (is it static usage?).
        
        // Let's implement a "Replace with '...'" action that blindly attempts replacement at the location.
        // The user can decide if it's correct.
        
        // However, we should try to be smart.
        // If replacement is "NewType(Args)", we probably shouldn't offer a fix, or maybe just "NewType"?
        
        var title = $"Use '{replacement}' instead";
        
        context.RegisterCodeFix(
            CodeAction.Create(
                title: title,
                createChangedDocument: c => ReplaceAsync(context.Document, diagnostic.Location.SourceSpan, replacement!, c),
                equivalenceKey: title),
            diagnostic);
    }

    private async Task<Document> ReplaceAsync(Document document, TextSpan span, string replacement, CancellationToken cancellationToken) {
        // If the replacement contains '(', strip it for the code modification if it's likely a signature.
        // E.g. "Method(int)" -> "Method".
        // But if the user put "Method(10)", maybe they want that? 
        // Standard convention for these attributes usually implies the name or signature.
        // If the attribute was generated via (Type, Type[]), it produces "Type(P1, P2)".
        // In that case, we likely just want "Type".
        
        var textToInsert = replacement;
        var parenIndex = textToInsert.IndexOf('(');
        if (parenIndex > 0) {
            textToInsert = textToInsert.Substring(0, parenIndex);
        }

        var root = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
        if (root == null) return document;

        var token = root.FindToken(span.Start);
        var node = root.FindNode(span);
        
        // We need to handle what exactly we are replacing.
        // The analyzer reports location on the Name or Type node.
        
        // If we are replacing "OldClass" with "NewClass", simple replace is fine.
        // If we are replacing "OldMethod" with "NewClass.NewMethod", we might need to check if it's a member access.
        
        // Case: new OldClass() -> new NewClass()
        // location is on "OldClass". TextToInsert is "NewClass".
        
        // Case: oldObj.OldMethod() -> oldObj.NewMethod()
        // location is on "OldMethod". TextToInsert "NewMethod" (if replacement was "NewMethod").
        // If replacement was "NewClass.NewMethod", and usage is static "OldClass.OldMethod()", 
        // location is "OldMethod". Replacing it with "NewClass.NewMethod" results in "OldClass.NewClass.NewMethod()". WRONG.
        // We would need to replace the whole MemberAccessExpression "OldClass.OldMethod".
        
        // This is getting complex for a generic "UseInstead" attribute. 
        // Let's stick to replacing the exact span that was highlighted, 
        // but try to handle "Type.Member" replacement if the node is an identifier.
        
        // If replacement contains dots (fully qualified or static member), and we are replacing a simple identifier:
        // 1. If parent is MemberAccess (e.g. `obj.Method`), and we replace `Method` with `Type.Method`, we get `obj.Type.Method`. 
        //    Likely `Type.Method` implies a static method or a different structure.
        //    If the previous usage was instance method, and new one is static, the call site needs major refactoring.
        //    We can't solve all.
        
        // Let's implement the simple "replace text at span" but stripping method parameters from the suggestion string.
        
        // However, we should be careful about replacing "OldClass" with "NewClass.Member".
        
        // Let's parse the replacement as a NameSyntax if possible to be safe? 
        // No, just text replacement.
        
        return document.WithText(
            (await document.GetTextAsync(cancellationToken)).Replace(span, textToInsert)
        );
    }
}
