﻿/*
    Copyright (C) 2014-2017 de4dot@gmail.com

    This file is part of dnSpy

    dnSpy is free software: you can redistribute it and/or modify
    it under the terms of the GNU General Public License as published by
    the Free Software Foundation, either version 3 of the License, or
    (at your option) any later version.

    dnSpy is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with dnSpy.  If not, see <http://www.gnu.org/licenses/>.
*/

using System.IO;
using System.Threading;
using dnSpy.Contracts.Debugger;
using dnSpy.Contracts.Debugger.CallStack;
using dnSpy.Contracts.Debugger.DotNet.Evaluation;
using dnSpy.Contracts.Debugger.DotNet.Evaluation.ValueNodes;
using dnSpy.Contracts.Debugger.DotNet.Text;
using dnSpy.Contracts.Debugger.Engine.Evaluation;
using dnSpy.Contracts.Debugger.Evaluation;
using dnSpy.Contracts.Text;
using dnSpy.Debugger.DotNet.Metadata;
using dnSpy.Roslyn.Shared.Properties;

namespace dnSpy.Roslyn.Shared.Debugger.ValueNodes {
	sealed class DynamicViewMembersValueNodeProvider : MembersValueNodeProvider {
		public override string ImageName => PredefinedDbgValueNodeImageNames.DynamicView;
		public override DbgDotNetText ValueText => valueText;

		static readonly DbgDotNetText valueText = new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Text, dnSpy_Roslyn_Shared_Resources.DebuggerVarsWindow_ExpandDynamicViewMessage));
		static readonly DbgDotNetText dynamicViewName = new DbgDotNetText(new DbgDotNetTextPart(BoxedTextColor.Text, dnSpy_Roslyn_Shared_Resources.DebuggerVarsWindow_DynamicView));

		readonly DbgDotNetValueNodeProviderFactory valueNodeProviderFactory;
		readonly DbgDotNetValue instanceValue;
		readonly string valueExpression;
		readonly DmdAppDomain appDomain;
		string dynamicViewProxyExpression;
		DbgDotNetValue getDynamicViewValue;

		public DynamicViewMembersValueNodeProvider(DbgDotNetValueNodeProviderFactory valueNodeProviderFactory, LanguageValueNodeFactory valueNodeFactory, DbgDotNetValue instanceValue, string valueExpression, DmdAppDomain appDomain, DbgValueNodeEvaluationOptions evalOptions)
			: base(valueNodeFactory, dynamicViewName, valueExpression + ", dynamic", default, evalOptions) {
			this.valueNodeProviderFactory = valueNodeProviderFactory;
			this.instanceValue = instanceValue;
			this.valueExpression = valueExpression;
			this.appDomain = appDomain;
			dynamicViewProxyExpression = string.Empty;
		}

		sealed class ForceLoadAssemblyState {
			public volatile int Counter;
		}

		protected override string InitializeCore(DbgEvaluationContext context, DbgStackFrame frame, CancellationToken cancellationToken) {
			if ((evalOptions & DbgValueNodeEvaluationOptions.NoFuncEval) != 0)
				return PredefinedEvaluationErrorMessages.FuncEvalDisabled;

			var proxyCtor = DynamicMetaObjectProviderDebugViewHelper.GetDynamicMetaObjectProviderDebugViewConstructor(appDomain);
			if ((object)proxyCtor == null) {
				var loadState = appDomain.GetOrCreateData<ForceLoadAssemblyState>();
				if (Interlocked.Exchange(ref loadState.Counter, 1) == 0) {
					var loader = new ReflectionAssemblyLoader(context, frame, appDomain, cancellationToken);
					if (loader.TryLoadAssembly(GetRequiredAssemblyFullName(context.Runtime)))
						proxyCtor = DynamicMetaObjectProviderDebugViewHelper.GetDynamicMetaObjectProviderDebugViewConstructor(appDomain);
				}
				if ((object)proxyCtor == null) {
					var asmFilename = GetRequiredAssemblyFilename(context.Runtime);
					var asm = appDomain.GetAssembly(Path.GetFileNameWithoutExtension(asmFilename));
					if (asm == null)
						return string.Format(dnSpy_Roslyn_Shared_Resources.DynamicViewAssemblyNotLoaded, asmFilename);
					return string.Format(dnSpy_Roslyn_Shared_Resources.TypeDoesNotExistInAssembly, DynamicMetaObjectProviderDebugViewHelper.GetDebugViewTypeDisplayName(), asmFilename);
				}
			}

			var runtime = context.Runtime.GetDotNetRuntime();
			var proxyTypeResult = runtime.CreateInstance(context, frame, proxyCtor, new[] { instanceValue }, DbgDotNetInvokeOptions.None, cancellationToken);
			if (proxyTypeResult.HasError)
				return proxyTypeResult.ErrorMessage;

			dynamicViewProxyExpression = valueNodeProviderFactory.GetNewObjectExpression(proxyCtor, valueExpression);
			getDynamicViewValue = proxyTypeResult.Value;
			valueNodeProviderFactory.GetMemberCollections(getDynamicViewValue.Type, evalOptions, out membersCollection, out _);
			return null;
		}

		// DNF 4.0:  "Microsoft.CSharp, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
		// DNC 1.0+: "Microsoft.CSharp, Version=4.0.X.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"
		string GetRequiredAssemblyFullName(DbgRuntime runtime) =>
			"Microsoft.CSharp, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a";

		string GetRequiredAssemblyFilename(DbgRuntime runtime) =>
			"Microsoft.CSharp.dll";

		protected override (DbgDotNetValueNode node, bool canHide) CreateValueNode(DbgEvaluationContext context, DbgStackFrame frame, int index, DbgValueNodeEvaluationOptions options, CancellationToken cancellationToken) =>
			CreateValueNode(context, frame, false, getDynamicViewValue.Type, getDynamicViewValue, index, options, dynamicViewProxyExpression, cancellationToken);

		protected override (DbgDotNetValueNode node, bool canHide) TryCreateInstanceValueNode(DbgEvaluationContext context, DbgStackFrame frame, in DbgDotNetValueResult valueResult, CancellationToken cancellationToken) {
			var noResultsNode = DebugViewNoResultsValueNode.TryCreate(context, frame, Expression, valueResult, cancellationToken);
			if (noResultsNode != null) {
				valueResult.Value?.Dispose();
				return (noResultsNode, false);
			}
			return (null, false);
		}

		protected override void DisposeCore() => getDynamicViewValue?.Dispose();
	}
}