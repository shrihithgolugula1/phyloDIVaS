using PhyloDivas.Data.New.Repositories;
using PhyloDivas.Data.New.Entities;


namespace FeedbackAPI
{
	public class FeedbackFormTableController : BaseCrudSingleKeyController<feedbackForm, long>
	{
		public FeedbackFormTableController(IUnitOfWork uow) : base(uow) { }
	}
}
