using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NotNot.Data;

public enum DeleteResult
{
	Error,
	/// <summary>
	/// The deletion was successful
	/// </summary>
	Success,
	/// <summary>
	/// The item was not found (maybe it was already deleted)
	/// </summary>
	NotFound,
}

//public enum CreateResult
//{
//   /// <summary>
//   /// Error
//   /// </summary>
//   None,
//   /// <summary>
//   /// 
//   /// </summary>
//   Success,
//   ///// <summary>
//   ///// The item was not found (maybe it was already deleted)
//   ///// </summary>
//   //NotFound,
//   ///// <summary>
//   ///// The item was found, but it could not be deleted
//   ///// </summary>
//   //Failed
//}

public enum UpsertResult
{
	Error,
	/// <summary>
	/// 
	/// </summary>
	Create,
	/// <summary>
	/// The item was not found (maybe it was already deleted)
	/// </summary>
	Update,
	///// <summary>
	///// The item was found, but it could not be deleted
	///// </summary>
	//Failed
}
