// Copyright 2011 The Go Authors. All rights reserved.
// Use of this source code is governed by a BSD-style
// license that can be found in the LICENSE file.

using System.Collections.Generic;

namespace CodeBrix.Ollama.ModelRunner; //was previously: golang/go src/text/template/funcs.go (BSD-3-Clause);

/// <summary>
/// A function a template may call.
/// </summary>
/// <param name="args">The evaluated arguments, in order.</param>
/// <returns>The function's result.</returns>
internal delegate object GoTemplateFunc(IReadOnlyList<object> args);
